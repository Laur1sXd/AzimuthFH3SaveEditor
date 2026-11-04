namespace AzimuthFH3;

internal sealed class EditorGrid : DataGridView
{
    int[] columnWeights = [];
    bool sizingColumns;

    public EditorGrid()
    {
        DoubleBuffered = true;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
    }

    public void SetColumnWeights(params int[] weights)
    {
        columnWeights = weights;
        SizeColumns();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        SizeColumns();
    }

    void SizeColumns()
    {
        if (sizingColumns || columnWeights.Length == 0) return;
        var visible = Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).ToArray();
        if (visible.Length != columnWeights.Length) return;
        int available = Math.Max(0, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        if (available == 0) return;
        int totalWeight = columnWeights.Sum(), remaining = available;
        sizingColumns = true;
        SuspendLayout();
        try
        {
            for (int i = 0; i < visible.Length; i++)
            {
                int width = i == visible.Length - 1 ? remaining : available * columnWeights[i] / totalWeight;
                visible[i].Width = Math.Max(visible[i].MinimumWidth, width);
                remaining -= visible[i].Width;
            }
        }
        finally { ResumeLayout(false); sizingColumns = false; }
    }
}
