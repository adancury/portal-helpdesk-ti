using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Services.Calendario
{
    public interface IHolidayProvider
    {
        /// <summary>Retorna feriados (por data, sem hora) para o ano informado.</summary>
        Task<ISet<DateTime>> GetHolidaysAsync(int year);
    }
}
