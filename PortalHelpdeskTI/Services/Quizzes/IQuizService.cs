// Caminho: Services/Quizzes/IQuizService.cs
using System;

namespace PortalHelpdeskTI.Services.Quizzes
{
    public interface IQuizService
    {
        PortalHelpdeskTI.ViewModels.Quizzes.QuizTakeVM MontarQuizParaExecucao(int quizId, int? seed = null);

        PortalHelpdeskTI.ViewModels.Quizzes.QuizResultadoVM CorrigirESalvar(
            int quizId,
            int usuarioId,
            PortalHelpdeskTI.ViewModels.Quizzes.QuizSubmitVM respostas,
            DateTime inicio
        );
    }
}

