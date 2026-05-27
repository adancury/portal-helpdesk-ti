using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.ViewModels.Comissoes
{
    public class ComissaoVendedorManutencaoVm
    {
        public string? Filtro { get; set; }
        public List<RowVm> Itens { get; set; } = new();

        public class RowVm
        {
            public int Id { get; set; }
            public int SlpCode { get; set; }
            public string SlpName { get; set; } = "";

            // somente leitura (vem do sync HANA -> SQL)
            public decimal Percentual { get; set; }
            public bool Ativo { get; set; }

            // editáveis no portal
            [Required]
            public string BaseCalculo { get; set; } = "FATURAMENTO";

            [Required]
            public string TipoVendedor { get; set; } = "REPRESENTANTE";

            [EmailAddress]
            public string? Email { get; set; }

            public bool ParticipaRelatorio { get; set; }

            // ✅ CONTROLA DESCONTO DE IR
            public bool DestacarIR { get; set; }
        }

        public class SaveInput
        {
            public List<RowSave> Itens { get; set; } = new();

            public class RowSave
            {
                public int Id { get; set; }
                public int SlpCode { get; set; }
                public string BaseCalculo { get; set; } = "";
                public string TipoVendedor { get; set; } = "";
                public string? Email { get; set; }
                public bool ParticipaRelatorio { get; set; }

                // ✅ RECEBIDO DO FRONT
                public bool DestacarIR { get; set; }
            }
        }
    }
}
