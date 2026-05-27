namespace PortalHelpdeskTI.Views.Relatorios
{
    public class StatusIndicadorLinhaVM
    {
        public int LogId { get; set; }
        public int Pedido { get; set; }
        public string CardCode { get; set; } = "";
        public string CardName { get; set; } = "";
        public string Criacao { get; set; } = "";
        public string Status { get; set; } = "";
        public string Atualizacao { get; set; } = "";
        public string Usuario { get; set; } = "";
        public string Tms { get; set; } = "";
        public string Vendedor { get; set; } = "";
    }

    public class StatusIndicadorPedidoVM
    {
        public int Pedido { get; set; }
        public string CardCode { get; set; } = "";
        public string CardName { get; set; } = "";
        public string Criacao { get; set; } = "";
        public List<StatusIndicadorLinhaVM> Logs { get; set; } = new();
    }
}
