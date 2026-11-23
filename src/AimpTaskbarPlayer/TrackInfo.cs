using System;

namespace AimpTaskbarPlayer;

public enum PlaybackState
{
    Stopped = 0,
    Paused = 1,
    Playing = 2,
}

/// <summary>Immutable snapshot of the track that AIMP reports through its shared memory block.</summary>
public sealed record TrackInfo(
    string Title,
    string Artist,
    string Album,
    string Year,
    string Genre,
    string FileName,
    int DurationMs,
    int BitRate,
    int Channels,
    int SampleRate,
    int TrackNumber)
{
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : (string.IsNullOrWhiteSpace(FileName) ? "Неизвестный трек" : System.IO.Path.GetFileNameWithoutExtension(FileName));

    public string DisplayArtist => !string.IsNullOrWhiteSpace(Artist) ? Artist : "Неизвестный исполнитель";

    public string FormatDescription
    {
        get
        {
            var ext = string.IsNullOrEmpty(FileName) ? "" : System.IO.Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(ext)) parts.Add(ext);
            if (BitRate > 0) parts.Add($"{BitRate} kbps");
            if (SampleRate > 0) parts.Add($"{SampleRate / 1000.0:0.#} kHz");
            if (Channels > 0) parts.Add(Channels == 1 ? "mono" : Channels == 2 ? "stereo" : $"{Channels} ch");
            return string.Join(" · ", parts);
        }
    }
}
