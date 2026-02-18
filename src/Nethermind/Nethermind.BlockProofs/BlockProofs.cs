// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Linq;
using Nethermind.Core.Crypto;
using Nethermind.Serialization.Ssz;

namespace Nethermind.BlockProofs;

[SszSerializable]
public struct SszBytes32
{
    [SszVector(32)]
    public byte[] Data { get; set; }

    public readonly ValueHash256 Hash => new(Data);

    public static SszBytes32 From(ValueHash256 hash) => new() { Data = hash.ToByteArray() };
}

[SszSerializable]
public struct HistoricalBatch
{
    [SszVector(8192)]
    public SszBytes32[] BlockRoots { get; set; }

    [SszVector(8192)]
    public SszBytes32[] StateRoots { get; set; }

    public static HistoricalBatch From(ValueHash256[] blockRoots, ValueHash256[] stateRoots) =>
        new()
        {
            BlockRoots = [.. blockRoots.Select(SszBytes32.From)],
            StateRoots = [.. stateRoots.Select(SszBytes32.From)]
        };
}

[SszSerializable]
public struct ValueHash256Vector
{
    [SszVector(8192)]
    public SszBytes32[] Data { get; set; }

    public static ValueHash256Vector From(ValueHash256[] hashesAccumulator) => new() { Data = [.. hashesAccumulator.Select(SszBytes32.From)] };

    public ValueHash256[] Hashes() => [.. Data.Select(x => x.Hash)];
}

[SszSerializable]
public struct BlockProofHistoricalHashesAccumulator
{
    [SszVector(15)]
    public SszBytes32[] Data { get; set; }

    public ValueHash256[] HashesAccumulator => [.. Data.Select(x => x.Hash)];

    public static BlockProofHistoricalHashesAccumulator From(ValueHash256[] hashesAccumulator) =>
        new()
        {
            Data = [.. hashesAccumulator.Select(SszBytes32.From)]
        };
}

[SszSerializable]
public struct BlockProofHistoricalRoots
{
    [SszVector(14)]
    public SszBytes32[] BeaconBlockProofData { get; set; }

    [SszVector(14)]
    public ValueHash256[] BeaconBlockProof { get; set; }

    public SszBytes32 BeaconBlockRootData { get; set; }
    public ValueHash256 BeaconBlockRoot => BeaconBlockRootData.Hash;

    [SszVector(11)]
    public SszBytes32[] ExecutionBlockProofData { get; set; }

    [SszVector(11)]
    public ValueHash256[] ExecutionBlockProof { get; set; }

    public long Slot { get; set; }

    public static BlockProofHistoricalRoots From(ValueHash256[] beaconBlockProof, ValueHash256 beaconBlockRoot, ValueHash256[] executionBlockProof, long slot)
    {
        return new BlockProofHistoricalRoots
        {
            BeaconBlockProofData = [.. beaconBlockProof.Select(SszBytes32.From)],
            BeaconBlockProof = beaconBlockProof,
            BeaconBlockRootData = SszBytes32.From(beaconBlockRoot),
            ExecutionBlockProofData = [.. executionBlockProof.Select(SszBytes32.From)],
            ExecutionBlockProof = executionBlockProof,
            Slot = slot
        };
    }
}

[SszSerializable]
public struct BlockProofHistoricalSummaries
{
    [SszVector(13)]
    public SszBytes32[] BeaconBlockProofData { get; set; }

    [SszVector(13)]
    public ValueHash256[] BeaconBlockProof { get; set; }

    public SszBytes32 BeaconBlockRootData { get; set; }
    public ValueHash256 BeaconBlockRoot => BeaconBlockRootData.Hash;

    [SszList(12)]
    public SszBytes32[] ExecutionBlockProofData { get; set; }

    [SszVector(12)]
    public ValueHash256[] ExecutionBlockProof { get; set; }

    public long Slot { get; set; }

    public static BlockProofHistoricalSummaries From(ValueHash256[] beaconBlockProof, ValueHash256 beaconBlockRoot, ValueHash256[] executionBlockProof, long slot)
    {
        return new BlockProofHistoricalSummaries
        {
            BeaconBlockProofData = [.. beaconBlockProof.Select(SszBytes32.From)],
            BeaconBlockProof = beaconBlockProof,
            BeaconBlockRootData = SszBytes32.From(beaconBlockRoot),
            ExecutionBlockProofData = [.. executionBlockProof.Select(SszBytes32.From)],
            ExecutionBlockProof = executionBlockProof,
            Slot = slot
        };
    }
}
