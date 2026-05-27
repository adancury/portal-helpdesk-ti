namespace PortalHelpdeskTI.Models
{
    using System.ComponentModel.DataAnnotations;

    public class RegistrarUsuarioViewModel
    {
        [Required, Display(Name = "Nome completo"), StringLength(120)]
        public string Nome { get; set; }

        [Required, EmailAddress, Display(Name = "E-mail")]
        public string Email { get; set; }

        [Required, Display(Name = "Ramal"), StringLength(10)]
        public string Ramal { get; set; }

        [Required, DataType(DataType.Password), Display(Name = "Senha")]
        [StringLength(100, MinimumLength = 8)]
        public string Senha { get; set; }

        [Required, DataType(DataType.Password), Display(Name = "Confirmar senha")]
        [Compare(nameof(Senha), ErrorMessage = "As senhas não conferem.")]
        public string ConfirmarSenha { get; set; }
    }

}
