namespace SpectraPlayer.Core.Playback.Mpv;

/// <summary>libmpv could not be loaded (missing DLL, or wrong architecture).</summary>
public sealed class MpvNotFoundException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A libmpv call returned an error code.</summary>
public sealed class MpvException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;
}
