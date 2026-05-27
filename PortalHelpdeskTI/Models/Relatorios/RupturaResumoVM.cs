namespace PortalHelpdeskTI.Models.Relatorios
{
    public class RupturaResumoVM
    {
        public int TotalItens { get; set; }

        public int QtdeAltoRisco { get; set; }
        public int QtdeMedioRisco { get; set; }
        public int QtdeBaixoRisco { get; set; }
        public int QtdeSemRisco { get; set; }
        public int QtdeRupturado { get; set; }

        // Opcional: % para exibir proporções, se quiser usar depois
        public decimal PercAltoRisco => TotalItens == 0 ? 0 : (decimal)QtdeAltoRisco / TotalItens * 100m;
        public decimal PercMedioRisco => TotalItens == 0 ? 0 : (decimal)QtdeMedioRisco / TotalItens * 100m;
        public decimal PercBaixoRisco => TotalItens == 0 ? 0 : (decimal)QtdeBaixoRisco / TotalItens * 100m;
        public decimal PercSemRisco => TotalItens == 0 ? 0 : (decimal)QtdeSemRisco / TotalItens * 100m;
        public decimal PercRupturado => TotalItens == 0 ? 0 : (decimal)QtdeRupturado / TotalItens * 100m;
    }
}
