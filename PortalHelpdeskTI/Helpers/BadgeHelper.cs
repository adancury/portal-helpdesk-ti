namespace PortalHelpdeskTI.Helpers
{
    public static class BadgeHelper
    {
        public static string GetStatusBadgeClass(string status)
        {
            return status switch
            {
                //"Aberto" => "bg-secondary",
                "Aberto" => "bg-info text-dark",
                "Em Atendimento" => "bg-warning text-dark",
                "Aguardando" => "bg-warning text-dark",
                "Concluído" => "bg-success",
                _ => "bg-light text-dark"
            };
        }

        public static string GetPrioridadeBadgeClass(string prioridade)
        {
            return prioridade switch
            {
                "Urgente" => "bg-danger",
                "Crítica" => "bg-danger",
                "Alta" => "bg-danger",
                "Média" => "bg-warning text-dark",
                "Baixa" => "bg-info text-dark",
                _ => "bg-secondary"
            };
        }
    }
}
