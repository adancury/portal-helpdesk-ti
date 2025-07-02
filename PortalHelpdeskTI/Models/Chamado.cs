using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PortalHelpdeskTI.Models
{
    public class Chamado
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Titulo { get; set; }

        [Required]
        public string Descricao { get; set; }

        [Required]
        public string Prioridade { get; set; }

        public DateTime DataAbertura { get; set; }
        public string Status { get; set; }

        [Required]
        public int UsuarioId { get; set; }
        public Usuario Usuario { get; set; }

        public int? TecnicoId { get; set; }
        public Usuario Tecnico { get; set; }

        public int? TipoChamadoId { get; set; }
        public TipoChamado TipoChamado { get; set; }

        public int? CategoriaId { get; set; }
        public CategoriaChamado Categoria { get; set; }

        public int? SubcategoriaId { get; set; }
        public SubcategoriaChamado Subcategoria { get; set; }

        public ICollection<Interacao> Interacoes { get; set; }
    }
}
