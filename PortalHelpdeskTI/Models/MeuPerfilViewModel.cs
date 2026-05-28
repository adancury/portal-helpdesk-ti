using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models;

public class MeuPerfilViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Informe seu nome.")]
    [Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Ramal")]
    public string? Ramal { get; set; }

    public string? Perfil { get; set; }

    public string? FotoPerfilPath { get; set; }

    [Display(Name = "Senha atual")]
    public string? SenhaAtual { get; set; }

    [MinLength(6, ErrorMessage = "A nova senha deve ter pelo menos 6 caracteres.")]
    [Display(Name = "Nova senha")]
    public string? NovaSenha { get; set; }

    [Compare(nameof(NovaSenha), ErrorMessage = "A confirmação não confere com a nova senha.")]
    [Display(Name = "Confirmar nova senha")]
    public string? ConfirmarNovaSenha { get; set; }

    [Display(Name = "Remover foto atual")]
    public bool RemoverFoto { get; set; }
}
