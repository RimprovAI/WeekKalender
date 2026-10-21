using System;
using WeekKalender.Core;

namespace WeekKalender.Providers
{
    /// <summary>Bron van afspraken. Wordt alleen aangeroepen op de achtergrondthread van <see cref="AppointmentService"/>.</summary>
    public interface IAppointmentProvider : IDisposable
    {
        string Name { get; }

        /// <summary>Afspraken in [start, end). Gooit geen uitzonderingen voor verwachte fouten maar geeft een ProviderResult terug.</summary>
        ProviderResult Fetch(DateTime start, DateTime end);
    }
}
