using System.Globalization;

namespace KareNot;

// Small dialog to change a note's type, text, status, severity and (for a BIP) its end time.
sealed class EditNoteDialog : DarkDialog
{
    readonly ComboBox kind = Dark(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 });
    readonly TextBox text = new() { Width = 420 };
    readonly ComboBox status = Dark(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 });
    readonly ComboBox severity = Dark(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 });
    readonly TextBox end = new() { Width = 120 };

    public string Kind => Kinds.All[kind.SelectedIndex];
    public string NoteText => text.Text;
    public string Status => Kinds.IsEdit(Kind) ? null : Statuses.All[status.SelectedIndex];
    public string Severity => Kinds.IsEdit(Kind) ? null : Severities.All[severity.SelectedIndex];

    // Seconds after the start, or null when empty / not a BIP.
    public double? End(double start) =>
        Kind == "BIP" && double.TryParse(end.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? start + d : null;

    public EditNoteDialog(Note note)
        : base("Notu düzenle — " + NoteStore.Fmt(note.T))
    {
        foreach (var k in Kinds.All) kind.Items.Add(Kinds.Label(k));
        kind.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds.All, note.Kind));
        text.Text = note.Text;
        foreach (var s in Statuses.All) status.Items.Add(Statuses.Label(s));
        status.SelectedIndex = Math.Max(0, Array.IndexOf(Statuses.All, note.Status));
        foreach (var s in Severities.All) severity.Items.Add(s == null ? "Yok" : Severities.Label(s));
        severity.SelectedIndex = Math.Max(0, Array.IndexOf(Severities.All, note.Severity));
        end.Text = note.End is double e && e > note.T ? (e - note.T).ToString("0.0", CultureInfo.InvariantCulture) : "";

        AddRow(Lbl("Tür"), kind);
        AddRow(Lbl("Not"), Field(text));
        AddRow(Lbl("Durum"), status);
        AddRow(Lbl("Önem"), severity);
        AddRow(Lbl("BİP süresi (sn)"), Field(end));
        AddButton("Kaydet", DialogResult.OK);

        void UpdateEnabled()
        {
            status.Enabled = severity.Enabled = !Kinds.IsEdit(Kind);
            end.Enabled = Kind == "BIP";
            end.BackColor = end.Enabled ? Theme.Field : Theme.Surface;
            end.Parent?.Invalidate();
        }
        kind.SelectedIndexChanged += (s, e) => UpdateEnabled();
        UpdateEnabled();
    }
}
