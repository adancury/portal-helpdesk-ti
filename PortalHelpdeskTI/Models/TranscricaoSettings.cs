namespace PortalHelpdeskTI.Models
{
    public class TranscricaoSettings
    {
        public string FfmpegPath { get; set; } = "ffmpeg"; // Ou caminho completo se necessário
        public string WhisperExePath { get; set; } = @"C:\IA\whisper.cpp\build\bin\Release\whisper-cli.exe";
        public string ModelPath { get; set; } = @"C:\IA\models\ggml-base.bin"; // Certifique-se que esse modelo existe nesse caminho
        public string Language { get; set; } = "pt";

        public string PythonExePath { get; set; } = "python";
        public bool DiarizationEnabled { get; set; } = true;
        public string DiarizeScriptPath { get; set; } = "diarize.py";
        public string? HuggingFaceToken { get; set; }
    }


}
