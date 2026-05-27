namespace PortalHelpdeskTI.Models.Relatorios
{
    namespace PortalHelpdeskTI.Models.Relatorios
    {
        public class ResumoLiberacaoUsuarioVM
        {
            public string Usuario { get; set; } = "";
            public int QtdeLiberado { get; set; }
            public int QtdeRejeitado { get; set; }

            public int Total => QtdeLiberado + QtdeRejeitado;
        }
    }

}
