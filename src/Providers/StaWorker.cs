using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WeekKalender.Core;

namespace WeekKalender.Providers
{
    /// <summary>
    /// Eén achtergrondthread (STA, zoals Outlook-COM verwacht) die opdrachten na elkaar uitvoert.
    /// De interface wacht hier nooit op; die krijgt een Task terug.
    /// </summary>
    public sealed class StaWorker : IDisposable
    {
        readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>();
        readonly Thread _thread;
        readonly bool _useMessageFilter;
        long _busySinceUtcTicks;

        public StaWorker(string name, bool useMessageFilter)
        {
            _useMessageFilter = useMessageFilter;
            _thread = new Thread(Loop) { IsBackground = true, Name = name };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        void Loop()
        {
            if (_useMessageFilter) MessageFilter.Register();
            try
            {
                foreach (Action work in _queue.GetConsumingEnumerable())
                {
                    Interlocked.Exchange(ref _busySinceUtcTicks, DateTime.UtcNow.Ticks);
                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Achtergrondtaak", ex);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _busySinceUtcTicks, 0);
                    }
                }
            }
            finally
            {
                if (_useMessageFilter) MessageFilter.Revoke();
            }
        }

        /// <summary>True als de huidige opdracht al langer dan <paramref name="limit"/> loopt (bijv. Outlook wacht op een melding).</summary>
        public bool IsBusyLongerThan(TimeSpan limit)
        {
            long since = Interlocked.Read(ref _busySinceUtcTicks);
            return since != 0 && DateTime.UtcNow - new DateTime(since, DateTimeKind.Utc) > limit;
        }

        public Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add(() =>
                {
                    try { tcs.TrySetResult(work()); }
                    catch (Exception ex) { tcs.TrySetException(ex); }
                });
            }
            catch (InvalidOperationException)
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(StaWorker)));
            }
            return tcs.Task;
        }

        public void Post(Action work)
        {
            try { _queue.Add(work); }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
            _thread.Join(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// COM-berichtfilter: als Outlook een aanroep tijdelijk weigert (bijvoorbeeld omdat er een melding openstaat),
    /// probeert COM het tot 30 seconden opnieuw in plaats van direct te falen.
    /// </summary>
    internal sealed class MessageFilter : IOleMessageFilter
    {
        const int SERVERCALL_ISHANDLED = 0;
        const int SERVERCALL_RETRYLATER = 2;
        const int PENDINGMSG_WAITDEFPROCESS = 2;
        const int RetryForMs = 30000;

        public static void Register()
        {
            IOleMessageFilter old;
            try { CoRegisterMessageFilter(new MessageFilter(), out old); }
            catch (Exception ex) { Log.Warn("COM-berichtfilter niet geregistreerd: " + ex.Message); }
        }

        public static void Revoke()
        {
            IOleMessageFilter old;
            try { CoRegisterMessageFilter(null, out old); }
            catch (Exception) { }
        }

        int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        {
            return SERVERCALL_ISHANDLED;
        }

        int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
        {
            if (dwRejectType == SERVERCALL_RETRYLATER && dwTickCount < RetryForMs) return 250;
            return -1;
        }

        int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        {
            return PENDINGMSG_WAITDEFPROCESS;
        }

        [DllImport("ole32.dll")]
        static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);
    }

    [ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }
}
