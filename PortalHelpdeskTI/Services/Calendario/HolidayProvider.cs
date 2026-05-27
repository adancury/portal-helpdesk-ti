using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PortalHelpdeskTI.Services.Calendario
{
    public class HolidayProvider : IHolidayProvider
    {
        private readonly IMemoryCache _cache;
        public HolidayProvider(IMemoryCache cache) => _cache = cache;

        public Task<ISet<DateTime>> GetHolidaysAsync(int year)
        {
            var key = $"feriados:global:{year}";
            if (_cache.TryGetValue(key, out ISet<DateTime> cached))
                return Task.FromResult(cached);

            var set = new HashSet<DateTime>();
            foreach (var d in FeriadosFixosBrasil(year)) set.Add(d.Date);
            foreach (var d in FeriadosMoveisBrasil(year)) set.Add(d.Date);

            _cache.Set(key, set, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
                SlidingExpiration = TimeSpan.FromHours(2)
            });

            return Task.FromResult<ISet<DateTime>>(set);
        }

        // Fixos nacionais
        private static IEnumerable<DateTime> FeriadosFixosBrasil(int year)
        {
            yield return new DateTime(year, 1, 1);   // Confraternização
            yield return new DateTime(year, 4, 21);  // Tiradentes
            yield return new DateTime(year, 5, 1);   // Dia do Trabalho
            yield return new DateTime(year, 9, 7);   // Independência
            yield return new DateTime(year, 10, 12); // N. Sra. Aparecida
            yield return new DateTime(year, 11, 2);  // Finados
            yield return new DateTime(year, 11, 15); // Proclamação da República
            yield return new DateTime(year, 12, 25); // Natal
        }

        // Móveis (Carnaval/sexta santa/Corpus Christi) por Páscoa (Butcher)
        private static IEnumerable<DateTime> FeriadosMoveisBrasil(int year)
        {
            var pascoa = DataPascoa(year);
            yield return pascoa.AddDays(-47).Date; // terça de carnaval
            yield return pascoa.AddDays(-2).Date;  // sexta-feira santa
            yield return pascoa.AddDays(60).Date;  // Corpus Christi
        }

        private static DateTime DataPascoa(int year)
        {
            int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
            int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }
    }
}
