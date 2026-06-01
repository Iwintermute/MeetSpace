using System;
using System.Collections.Generic;
using System.Linq;
using MeetSpace.Client.App.Calls;

namespace MeetSpace.Client.App.Diagnostics;

public sealed record TrackQualityMetrics(
    string ProducerId,
    string Kind,
    string? TrackType,
    double PacketLossPercent,
    double BitrateKbps,
    bool Paused);

public sealed record CallQualityReport(
    DateTimeOffset Timestamp,
    string? SessionId,
    string? RoomId,
    IReadOnlyList<TrackQualityMetrics> Tracks,
    double OverallHealthScore,
    string HealthLabel);

public sealed class CallQualityTracker
{
    private CallQualityReport? _lastReport;

    public CallQualityReport? LastReport => _lastReport;
    public event EventHandler<CallQualityReport>? QualityReportReady;

    public CallQualityReport? Update(MediaStatsSnapshot snapshot, string? sessionId = null)
    {
        if (snapshot == null || snapshot.Producers == null || snapshot.Producers.Count == 0)
            return null;

        var tracks = new List<TrackQualityMetrics>();
        foreach (var producer in snapshot.Producers)
        {
            if (string.IsNullOrWhiteSpace(producer.ProducerId))
                continue;

            tracks.Add(new TrackQualityMetrics(
                producer.ProducerId,
                producer.Kind ?? "audio",
                producer.TrackType,
                NormalizeMetric(producer.PacketLossPercent),
                NormalizeMetric(producer.BitrateKbps),
                producer.Paused));
        }

        if (tracks.Count == 0)
            return null;

        return BuildAndPublishReport(DateTimeOffset.UtcNow, sessionId, snapshot.RoomId, tracks);
    }

    public CallQualityReport? UpdateFromBridge(CallQualitySnapshot snapshot, string? sessionId = null, string? roomId = null)
    {
        if (snapshot == null || snapshot.Tracks == null || snapshot.Tracks.Count == 0)
            return null;

        var tracks = new List<TrackQualityMetrics>(snapshot.Tracks.Count);
        for (var index = 0; index < snapshot.Tracks.Count; index++)
        {
            var sample = snapshot.Tracks[index];
            var trackType = sample.Kind.Equals("video", StringComparison.OrdinalIgnoreCase)
                ? "camera"
                : sample.Kind.Equals("audio", StringComparison.OrdinalIgnoreCase)
                    ? "microphone"
                    : sample.Kind;

            tracks.Add(new TrackQualityMetrics(
                $"bridge:{sample.Direction}:{sample.Kind}:{index}",
                sample.Kind,
                trackType,
                NormalizeMetric(sample.PacketLossPercent),
                NormalizeMetric(sample.BitrateKbps),
                false));
        }

        return BuildAndPublishReport(snapshot.Timestamp, sessionId, roomId, tracks);
    }

    public void Reset()
    {
        _lastReport = null;
    }

    private CallQualityReport BuildAndPublishReport(
        DateTimeOffset timestamp,
        string? sessionId,
        string? roomId,
        IReadOnlyList<TrackQualityMetrics> tracks)
    {
        var healthScore = ComputeHealthScore(tracks);
        var report = new CallQualityReport(
            timestamp,
            sessionId,
            roomId,
            tracks,
            Math.Round(healthScore, 3),
            ScoreToLabel(healthScore));

        _lastReport = report;
        QualityReportReady?.Invoke(this, report);
        return report;
    }

    private static double ComputeHealthScore(IReadOnlyList<TrackQualityMetrics> tracks)
    {
        if (tracks == null || tracks.Count == 0)
            return 0;

        var activeTracks = tracks.Where(static x => !x.Paused).ToList();
        if (activeTracks.Count == 0)
            return 0.1;

        var pausedRatio = (tracks.Count - activeTracks.Count) / (double)tracks.Count;
        var maxPacketLoss = activeTracks.Max(static x => x.PacketLossPercent);
        var avgPacketLoss = activeTracks.Average(static x => x.PacketLossPercent);
        var avgBitrate = activeTracks.Average(static x => x.BitrateKbps);

        var score = 1.0;
        score -= Math.Min(0.25, pausedRatio * 0.35);
        score -= Math.Min(0.45, maxPacketLoss / 100.0 * 0.9);
        score -= Math.Min(0.2, avgPacketLoss / 100.0 * 0.5);
        if (avgBitrate <= 0)
            score -= 0.2;
        else if (avgBitrate < 80)
            score -= 0.15;
        else if (avgBitrate < 180)
            score -= 0.08;

        if (score < 0)
            return 0;
        if (score > 1)
            return 1;
        return score;
    }

    private static string ScoreToLabel(double score)
    {
        if (score >= 0.85) return "excellent";
        if (score >= 0.7) return "good";
        if (score >= 0.5) return "fair";
        if (score >= 0.3) return "poor";
        return "critical";
    }

    private static double NormalizeMetric(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return 0;
        return value;
    }
}
