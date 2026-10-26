"""vid2sim pipeline: phone video in, Unity-ready procedure bundle out.

Stages, in order: transcribe -> extract -> segment -> bundle -> generate -> package.
Every stage logs wall-clock and cost to metrics/runs.csv through common.Metrics.
"""
