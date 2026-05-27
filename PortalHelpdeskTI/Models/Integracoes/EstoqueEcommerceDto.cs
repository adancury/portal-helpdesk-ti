namespace PortalHelpdeskTI.Models.Integracoes
{
    public class EstoqueEcommerceDto
    {
        public string ItemCode { get; set; } = "";
        public string WhsCode { get; set; } = "";
        public int OnHand { get; set; }
        public int IsCommited { get; set; }
        public int AvailableQuantity { get; set; }
        public int LastSentStock { get; set; }
    }
    public class EstoqueEcommerceMultiDepositoDto
    {
        public string ItemCode { get; set; } = "";
        public List<EstoqueEcommercePorDepositoDto> Depositos { get; set; } = new();
    }

    public class EstoqueEcommercePorDepositoDto
    {
        public string WhsCode { get; set; } = "";
        public int OnHand { get; set; }
        public int IsCommited { get; set; }
        public int AvailableQuantity { get; set; }
        public int LastSentStock { get; set; }
    }
}
