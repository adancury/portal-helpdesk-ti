namespace PortalHelpdeskTI.ViewModels.IntegracoesWms
{
    public class IntegracoesWmsResumoItemVm
    {
        public string Chave { get; set; } = "";
        public string Titulo { get; set; } = "";
        public int Quantidade { get; set; }
        public string Tipo { get; set; } = "";
        public int Ok { get; set; }
        public int Erro { get; set; }
        public int Neutro { get; set; }
        public int Tentativas { get; set; }
    }

    public class IntegracoesWmsResumoVm
    {
        public string Aba { get; set; } = "";
        public List<IntegracoesWmsResumoItemVm> Itens { get; set; } = new();
        public int TotalGeral => Itens.Sum(x => x.Quantidade);
    }
}
