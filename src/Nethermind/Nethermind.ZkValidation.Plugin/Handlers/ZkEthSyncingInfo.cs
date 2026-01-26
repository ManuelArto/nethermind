// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Diagnostics;
using Nethermind.Blockchain;
using Nethermind.Facade.Eth;
using Nethermind.Synchronization.ParallelSync;

namespace Nethermind.ZkValidation.Plugin.Handlers;
public class ZkEthSyncingInfo(IBlockTree blockTree, ISyncModeSelector syncModeSelector): IEthSyncingInfo
{

    public SyncingResult GetFullInfo()
    {
        // Always report as synced if we have a valid head
        if (blockTree.Head != null && blockTree.Head.Number > 0)
            return SyncingResult.NotSyncing;

        return ReturnSyncing(blockTree.Head?.Number ?? 0, blockTree.Head?.Number ?? 0, syncModeSelector.Current);
    }

    private static SyncingResult ReturnSyncing(long headNumberOrZero, long bestSuggestedNumber, SyncMode syncMode)
    {
        return new SyncingResult
        {
            CurrentBlock = headNumberOrZero,
            HighestBlock = bestSuggestedNumber,
            StartingBlock = 0L,
            SyncMode = syncMode,
            IsSyncing = true
        };
    }

    private readonly Stopwatch _syncStopwatch = new();

    public TimeSpan UpdateAndGetSyncTime()
    {
        if (!_syncStopwatch.IsRunning)
        {
            if (IsSyncing())
            {
                _syncStopwatch.Start();
            }
            return TimeSpan.Zero;
        }

        if (!IsSyncing())
        {
            _syncStopwatch.Stop();
            return TimeSpan.Zero;
        }

        return _syncStopwatch.Elapsed;
    }

    public SyncMode SyncMode => syncModeSelector.Current;

    public bool IsSyncing()
    {
        return GetFullInfo().IsSyncing;
    }
}
