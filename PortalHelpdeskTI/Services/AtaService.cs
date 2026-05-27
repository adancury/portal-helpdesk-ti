using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom; // <- pode REMOVER se quiser; só mantenha se realmente usar PageSize
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using PortalHelpdeskTI.Services;
using PathIO = System.IO.Path;
// e usar: PathIO.Combine(...)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// Alias para evitar confusão com iText.Layout.Element.List
using ItList = iText.Layout.Element.List;

public class AtaService
{
    public async Task<string> GerarPdfEstruturadoAsync(
        string diretorio,
        string titulo,
        IEnumerable<string> participantesManuais,
        DateTime inicio,
        DateTime fim,
        ParsedMinutes parsed,
        string? arquivoAudioRelativo = null
    )
    {
        // use sempre System.IO.Path para evitar ambiguidade
        Directory.CreateDirectory(diretorio);
        var pdfPath = System.IO.Path.Combine(diretorio, "ata.pdf");

        using var writer = new PdfWriter(pdfPath);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf, PageSize.A4, false);

        var regular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        // Cabeçalho
        doc.Add(new Paragraph("ATA DE REUNIÃO")
            .SetFont(bold).SetFontSize(16)
            .SetTextAlignment(TextAlignment.CENTER));

        doc.Add(new Paragraph($"Título: {titulo}")
            .SetFont(bold).SetFontSize(12));
        doc.Add(new Paragraph($"Início: {inicio:G}    |    Término: {fim:G}")
            .SetFont(regular).SetFontSize(10));

        // Participantes (combina parser + manual)
        var participantes = new List<string>();
        if (participantesManuais != null)
            participantes.AddRange(participantesManuais.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (parsed.Participants?.Count > 0)
            participantes.AddRange(parsed.Participants);

        participantes = participantes
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (participantes.Count > 0)
        {
            doc.Add(new Paragraph("\nParticipantes").SetFont(bold).SetFontSize(12));

            var lst = new ItList();                 // sem numeração
            lst.SetListSymbol("• ");                // bullet
            foreach (var p in participantes)
            {
                var li = new ListItem();
                li.Add(new Paragraph(p).SetFont(regular));
                lst.Add(li);
            }
            doc.Add(lst);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Objective))
        {
            doc.Add(new Paragraph("\nObjetivo").SetFont(bold).SetFontSize(12));
            doc.Add(new Paragraph(parsed.Objective).SetFont(regular));
        }

        // Tópicos
        if (parsed.Topics != null && parsed.Topics.Count > 0)
        {
            doc.Add(new Paragraph("\nTópicos").SetFont(bold).SetFontSize(12));
            int idx = 1;
            foreach (var t in parsed.Topics)
            {
                doc.Add(new Paragraph($"{idx}. {t.Title}").SetFont(bold).SetFontSize(11));

                if (t.Notes != null && t.Notes.Count > 0)
                {
                    var notes = new ItList();
                    notes.SetListSymbol("• ");
                    foreach (var n in t.Notes)
                    {
                        var li = new ListItem();
                        li.Add(new Paragraph(n).SetFont(regular));
                        notes.Add(li);
                    }
                    doc.Add(notes);
                }

                if (t.Decisions != null && t.Decisions.Count > 0)
                {
                    doc.Add(new Paragraph("Decisões").SetFont(bold).SetFontSize(10));
                    var decs = new ItList(ListNumberingType.DECIMAL);
                    foreach (var d in t.Decisions)
                    {
                        var li = new ListItem();
                        li.Add(new Paragraph(d).SetFont(regular));
                        decs.Add(li);
                    }
                    doc.Add(decs);
                }

                if (t.Actions != null && t.Actions.Count > 0)
                {
                    doc.Add(new Paragraph("Ações").SetFont(bold).SetFontSize(10));
                    AddActionsTable(doc, t.Actions, regular, bold);
                }

                if (t.Pendings != null && t.Pendings.Count > 0)
                {
                    doc.Add(new Paragraph("Pendências").SetFont(bold).SetFontSize(10));
                    var pens = new ItList(ListNumberingType.DECIMAL);
                    foreach (var p in t.Pendings)
                    {
                        var li = new ListItem();
                        li.Add(new Paragraph(p).SetFont(regular));
                        pens.Add(li);
                    }
                    doc.Add(pens);
                }

                idx++;
            }
        }

        // Seções globais (fora dos tópicos)
        if (parsed.GlobalDecisions != null && parsed.GlobalDecisions.Count > 0)
        {
            doc.Add(new Paragraph("\nDecisões (Gerais)").SetFont(bold).SetFontSize(12));
            var decs = new ItList(ListNumberingType.DECIMAL);
            foreach (var d in parsed.GlobalDecisions)
            {
                var li = new ListItem();
                li.Add(new Paragraph(d).SetFont(regular));
                decs.Add(li);
            }
            doc.Add(decs);
        }

        if (parsed.GlobalActions != null && parsed.GlobalActions.Count > 0)
        {
            doc.Add(new Paragraph("\nAções (Gerais)").SetFont(bold).SetFontSize(12));
            AddActionsTable(doc, parsed.GlobalActions, regular, bold);
        }

        if (parsed.GlobalPendings != null && parsed.GlobalPendings.Count > 0)
        {
            doc.Add(new Paragraph("\nPendências (Gerais)").SetFont(bold).SetFontSize(12));
            var pens = new ItList(ListNumberingType.DECIMAL);
            foreach (var p in parsed.GlobalPendings)
            {
                var li = new ListItem();
                li.Add(new Paragraph(p).SetFont(regular));
                pens.Add(li);
            }
            doc.Add(pens);
        }

        if (parsed.Notes != null && parsed.Notes.Count > 0)
        {
            doc.Add(new Paragraph("\nNotas").SetFont(bold).SetFontSize(12));
            var notes = new ItList();
            notes.SetListSymbol("• ");
            foreach (var n in parsed.Notes)
            {
                var li = new ListItem();
                li.Add(new Paragraph(n).SetFont(regular));
                notes.Add(li);
            }
            doc.Add(notes);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Closing))
        {
            doc.Add(new Paragraph("\nEncerramento").SetFont(bold).SetFontSize(12));
            doc.Add(new Paragraph(parsed.Closing!).SetFont(regular));
        }

        if (!string.IsNullOrWhiteSpace(arquivoAudioRelativo))
        {
            doc.Add(new Paragraph($"\nÁudio da reunião: {arquivoAudioRelativo}")
                .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.GRAY));
        }

        await Task.CompletedTask;
        return pdfPath;
    }

    private void AddActionsTable(Document doc, List<ActionItem> actions, PdfFont regular, PdfFont bold)
    {
        var table = new Table(new float[] { 5, 3, 2 }).UseAllAvailableWidth();
        AddCell(table, "Descrição", bold, true);
        AddCell(table, "Responsável", bold, true);
        AddCell(table, "Prazo", bold, true);

        foreach (var a in actions)
        {
            AddCell(table, a.Description ?? "-", regular);
            AddCell(table, a.Responsible ?? "-", regular);
            var prazo = a.DueDate.HasValue ? a.DueDate.Value.ToString("dd/MM/yyyy")
                      : (!string.IsNullOrWhiteSpace(a.RawDue) ? a.RawDue : "-");
            AddCell(table, prazo, regular);
        }

        doc.Add(table);
    }

    private void AddCell(Table table, string text, PdfFont font, bool header = false)
    {
        var cell = new Cell().Add(new Paragraph(text).SetFont(font).SetFontSize(header ? 10 : 9));
        if (header) cell.SetBackgroundColor(new DeviceRgb(240, 240, 240));
        cell.SetPadding(6);
        table.AddCell(cell);
    }
}
