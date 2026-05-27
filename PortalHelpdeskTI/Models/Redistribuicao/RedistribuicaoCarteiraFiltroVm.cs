using System.ComponentModel.DataAnnotations;

public class RedistribuicaoCarteiraFiltroVm
{
    [Range(1, 24)]
    public int MesesLead { get; set; } = 3;

    [Range(1, 60)]
    public int MesesInativo { get; set; } = 12;

    [Range(1, 24)]
    public int JanelaTkmMeses { get; set; } = 12;

    [Range(1, 3650)]
    public int DiasInativo { get; set; } = 180;

    public bool IncluirSomenteAtivos { get; set; } = true;

    // 👇 ADICIONAR AQUI
    public int DiasInatividadeCalculado
    {
        get
        {
            if (DiasInativo > 0)
                return DiasInativo;

            if (MesesInativo >= 30)
                return MesesInativo; // interpretado como dias

            return MesesInativo * 30;
        }
    }
}

