using System.Text.RegularExpressions;
namespace PortalHelpdeskTI.Services
{
    public enum SectionType
    {
        Header,
        Participants,
        Topic,
        Decision,
        Action,
        Pending,
        Note,
        Closing,
        Unknown
    }

    public class ActionItem
    {
        public string Description { get; set; } = "";
        public string? Responsible { get; set; }
        public DateTime? DueDate { get; set; }
        public string? RawDue { get; set; } // guarda texto do prazo quando não parseado
    }

    public class TopicSection
    {
        public string Title { get; set; } = "";
        public List<string> Notes { get; set; } = new();
        public List<string> Decisions { get; set; } = new();
        public List<ActionItem> Actions { get; set; } = new();
        public List<string> Pendings { get; set; } = new();
    }

    public class ParsedMinutes
    {
        public string? Objective { get; set; }
        public List<string> Participants { get; set; } = new();
        public List<TopicSection> Topics { get; set; } = new();
        public List<string> GlobalDecisions { get; set; } = new();
        public List<ActionItem> GlobalActions { get; set; } = new();
        public List<string> GlobalPendings { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        public string? Closing { get; set; }
    }

    public static class MinutesParser
    {
        // Palavras‑chave (flexíveis em pt-BR)
        private static readonly Regex RxTopic =
            new(@"^\s*(t[óo]pico|assunto|tema|pauta)\s*[:\-]\s*(.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxDecision =
            new(@"^\s*(decis(ã|a)o|ficou decidido|foi acordado|delibera(ç|c)[aã]o|encaminhamento)\s*[:\-]?\s*(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxAction =
            new(@"^\s*(a[cç][ãa]o|tarefa|pr[óo]ximo passo|atividade)\s*[:\-]?\s*(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxPending =
            new(@"^\s*(pend[êe]ncia|em aberto|a resolver)\s*[:\-]?\s*(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxParticipants =
            new(@"^\s*(participantes|presentes)\s*[:\-]\s*(.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxObjective =
            new(@"^\s*(objetivo|prop[óo]sito)\s*[:\-]\s*(.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex RxClosing =
            new(@"^\s*(encerramento|finaliza(ç|c)[aã]o|nada mais havendo)\s*[:\-]?\s*(.*)$", RegexOptions.IgnoreCase);

        // Reconhece responsavel/prazo na mesma linha ou linhas seguintes
        private static readonly Regex RxResponsible =
            new(@"respons[aá]vel\s*[:\-]\s*([^\.;\|]+)", RegexOptions.IgnoreCase);
        private static readonly Regex RxDeadlineDate =
            new(@"prazo\s*[:\-]\s*(\d{1,2}/\d{1,2}/\d{2,4})", RegexOptions.IgnoreCase);
        private static readonly Regex RxDeadlineFreeText =
            new(@"prazo\s*[:\-]\s*([^\.;\|]+)", RegexOptions.IgnoreCase);

        // Bullets e timestamps
        private static readonly Regex RxBullet =
            new(@"^\s*([\-\*\u2022]|•|\d+\.)\s*(.+)$");
        private static readonly Regex RxTimestampSpeaker =
            new(@"^\s*(\[\d{1,2}:\d{2}\])?\s*([A-Za-zÀ-ÖØ-öø-ÿ'.\-\s]{2,30})?\s*:\s*(.+)$");

        public static ParsedMinutes Parse(string transcription)
        {
            var result = new ParsedMinutes();
            if (string.IsNullOrWhiteSpace(transcription)) return result;

            // Normalização básica
            var lines = transcription
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            TopicSection? currentTopic = null;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Extrai participantes / objetivo / encerramento (globais)
                if (TryMatch(RxParticipants, line, out var mPar))
                {
                    var list = mPar.Groups[2].Value;
                    result.Participants.AddRange(SplitParticipants(list));
                    continue;
                }
                if (TryMatch(RxObjective, line, out var mObj))
                {
                    result.Objective = mObj.Groups[2].Value.Trim();
                    continue;
                }
                if (TryMatch(RxClosing, line, out var mClose))
                {
                    var val = (mClose.Groups[2].Success ? mClose.Groups[2].Value : "").Trim();
                    result.Closing = string.IsNullOrWhiteSpace(val) ? "Reunião encerrada." : val;
                    continue;
                }

                // Abre novo tópico
                if (TryMatch(RxTopic, line, out var mTop))
                {
                    currentTopic = new TopicSection
                    {
                        Title = SafeText(mTop.Groups[2].Value)
                    };
                    result.Topics.Add(currentTopic);
                    continue;
                }

                // Decisão
                if (TryMatch(RxDecision, line, out var mDec))
                {
                    var text = TakeBulletOrLine(mDec.Groups[3].Value, line);
                    if (currentTopic != null) currentTopic.Decisions.Add(text);
                    else result.GlobalDecisions.Add(text);
                    continue;
                }

                // Ação (pode ter resp/prazo)
                if (TryMatch(RxAction, line, out var mAct))
                {
                    var action = BuildActionItem(mAct.Groups[2].Value, Peek(lines, i + 1), out var consumedNext);

                    if (currentTopic != null) currentTopic.Actions.Add(action);
                    else result.GlobalActions.Add(action);

                    if (consumedNext) i++; // consumiu linha seguinte para captar responsável/prazo
                    continue;
                }

                // Pendência
                if (TryMatch(RxPending, line, out var mPen))
                {
                    var text = TakeBulletOrLine(mPen.Groups[2].Value, line);
                    if (currentTopic != null) currentTopic.Pendings.Add(text);
                    else result.GlobalPendings.Add(text);
                    continue;
                }

                // Caso nenhuma keyword: nota / fala
                var note = ExtractNote(line);
                if (currentTopic != null) currentTopic.Notes.Add(note);
                else result.Notes.Add(note);
            }

            return result;
        }

        private static bool TryMatch(Regex rx, string line, out Match m)
        {
            m = rx.Match(line);
            return m.Success;
        }

        private static IEnumerable<string> SplitParticipants(string raw)
        {
            return raw.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(p => p.Trim())
                      .Where(p => p.Length > 0);
        }

        private static string SafeText(string s) => (s ?? "").Trim();

        private static string Peek(List<string> lines, int index) =>
            (index >= 0 && index < lines.Count) ? lines[index] : "";

        private static string TakeBulletOrLine(string capture, string wholeLine)
        {
            var text = SafeText(capture);
            if (string.IsNullOrWhiteSpace(text))
            {
                // se não veio no grupo, pega a parte após o marcador
                var m = RxBullet.Match(wholeLine);
                if (m.Success && m.Groups.Count > 2)
                    text = m.Groups[2].Value.Trim();
            }
            return string.IsNullOrWhiteSpace(text) ? SafeText(wholeLine) : text;
        }

        private static string ExtractNote(string line)
        {
            // Remove marcador e mantém timestamp/falante em itálico no PDF (vamos só marcar aqui)
            var m = RxBullet.Match(line);
            if (m.Success && m.Groups.Count > 2)
                line = m.Groups[2].Value.Trim();

            // mantém timestamp/falante como parte da nota
            return SafeText(line);
        }

        private static ActionItem BuildActionItem(string baseText, string nextLine, out bool consumedNext)
        {
            consumedNext = false;
            var item = new ActionItem { Description = SafeText(baseText) };

            // Tenta capturar resp/prazo na própria linha
            CaptureRespAndDeadline(baseText, item);

            // Se não encontrou, tenta na próxima linha (caso formato em duas linhas)
            if (item.Responsible == null && item.DueDate == null && !string.IsNullOrWhiteSpace(nextLine))
            {
                var next = nextLine.Trim();
                if (RxResponsible.IsMatch(next) || RxDeadlineDate.IsMatch(next) || RxDeadlineFreeText.IsMatch(next))
                {
                    CaptureRespAndDeadline(next, item);
                    consumedNext = true;
                }
            }

            return item;
        }

        private static void CaptureRespAndDeadline(string text, ActionItem item)
        {
            var mResp = RxResponsible.Match(text);
            if (mResp.Success)
                item.Responsible = mResp.Groups[1].Value.Trim();

            var mDate = RxDeadlineDate.Match(text);
            if (mDate.Success)
            {
                if (TryParsePtBrDate(mDate.Groups[1].Value, out var dt))
                    item.DueDate = dt;
            }
            else
            {
                var mFree = RxDeadlineFreeText.Match(text);
                if (mFree.Success)
                    item.RawDue = mFree.Groups[1].Value.Trim();
            }
        }

        private static bool TryParsePtBrDate(string s, out DateTime dt)
        {
            var ok = DateTime.TryParseExact(s.Trim(),
                new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy", "dd/MM/yy" },
                new System.Globalization.CultureInfo("pt-BR"),
                System.Globalization.DateTimeStyles.None,
                out dt);
            return ok;
        }
    }

}
