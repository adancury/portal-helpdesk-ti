namespace PortalHelpdeskTI.Models.Comissoes
{
    public class OsLpRow
    {
        public int SlpCode { get; set; }
        public string SlpName { get; set; } = "";
        public string? Active { get; set; }   // 'Y'/'N'
        public string? E_Mail { get; set; }   // OSLP.E_Mail
    }
}
