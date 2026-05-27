using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models.Relatorios
{
    public class MudancaCarteiraTabsVM
    {
        // Mantém a aba selecionada após POST
        public string ActiveTab { get; set; } = "carteira"; // "carteira" | "inativar"

        // Reaproveita seu VM atual (com SapUser/SapPassword/Arquivo/AtualizarPrincipal)
        public MudancaCarteiraServiceLayerServiceVM Carteira { get; set; } = new();

        // Aba 2: Inativação de BP (reutiliza as mesmas credenciais do bloco Carteira)
        [Required(ErrorMessage = "Informe o CardCode do Parceiro de Negócio.")]
        public string CardCode { get; set; } = string.Empty;

        // Opcional: confirmação para evitar clique acidental
        public bool ConfirmarInativacao { get; set; } = false;

        // Retornos para UI (opcional, mas muito útil)
        public bool? SucessoInativacao { get; set; }
        public string? MensagemInativacao { get; set; }

        public bool? SucessoCarteira { get; set; }
        public string? MensagemCarteira { get; set; }
    }
}
