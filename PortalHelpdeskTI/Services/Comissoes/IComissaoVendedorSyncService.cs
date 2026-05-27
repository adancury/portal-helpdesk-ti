namespace PortalHelpdeskTI.Services.Comissoes
{
    public interface IComissaoVendedorSyncService
    {
        Task<(int Inseridos, int Atualizados)> SyncHanaToSqlAsync(CancellationToken ct = default);

        /// <summary>
        /// Só atualiza o HANA se o E_Mail estiver vazio no HANA e o portalEmail estiver preenchido.
        /// </summary>
        Task<bool> TryPushEmailToHanaIfEmptyAsync(int slpCode, string portalEmail, CancellationToken ct = default);
    }
}
