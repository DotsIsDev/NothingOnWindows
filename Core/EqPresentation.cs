namespace NothingOnWindows.Core;

public static class EqPresentation
{
    // Relative radar-chart weights from the community's B186 UI reference.
    // These shape the preset illustration; they are not measured DSP gains.
    public static float[] Preview(byte? preset, float[] custom)
    {
        if (preset == 5) return custom.ToArray();
        float[]? weights = preset switch
        {
            0 => [6, 5, 6],
            3 => [10, 3, 4],
            2 => [3, 3, 10],
            1 => [3, 10, 3],
            _ => null
        };
        return weights?.Select(w => (w - 5) * 1.2f).ToArray() ?? [0, 0, 0];
    }
}
