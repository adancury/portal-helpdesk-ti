namespace PortalHelpdeskTI.Models.Comissoes
{
    public class ComissaoEnvioLog
    {
        public int Id { get; set; }
        public int Ano { get; set; }
        public int Mes { get; set; }
        public int SlpCode { get; set; }
        public string SlpName { get; set; } = "";

        public string EmailPara { get; set; } = "";
        public DateTime DataEnvio { get; set; } = DateTime.Now;

        public bool Sucesso { get; set; }
        public string? Erro { get; set; }
    }
}
