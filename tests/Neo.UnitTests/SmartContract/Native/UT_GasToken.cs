// Copyright (C) 2015-2025 The Neo Project.
//
// UT_GasToken.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

using Neo.Extensions.IO;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.UnitTests.Extensions;
using System.Numerics;

namespace Neo.UnitTests.SmartContract.Native;

[TestClass]
public class UT_GasToken
{
    private DataCache _snapshotCache = null!;

    [TestInitialize]
    public void TestSetup()
    {
        _snapshotCache = TestBlockchain.GetTestSnapshotCache();
    }

    [TestMethod]
    public void Check_Name() => Assert.AreEqual("GasToken", Governance.GasTokenName);

    [TestMethod]
    public void Check_Symbol() => Assert.AreEqual("GAS", Governance.GasTokenSymbol);

    [TestMethod]
    public void Check_Decimals() => Assert.AreEqual(8, Governance.GasTokenDecimals);

    [TestMethod]
    public async Task Check_BalanceOfTransferAndBurn()
    {
        var snapshot = _snapshotCache.CloneCache();
        var persistingBlock = new Block
        {
            Header = new Header
            {
                PrevHash = UInt256.Zero,
                MerkleRoot = UInt256.Zero,
                Index = 1000,
                NextConsensus = UInt160.Zero,
                Witness = null!
            },
            Transactions = []
        };
        byte[] from = Contract.GetBFTAddress(TestProtocolSettings.Default.StandbyValidators).ToArray();
        byte[] to = new byte[20];
        var tokenInfo = NativeContract.TokenManagement.GetTokenInfo(snapshot, NativeContract.Governance.GasTokenId);
        var supply = tokenInfo!.TotalSupply;
        Assert.AreEqual(5200000050000000, supply); // 3000000000000000 + 50000000 (neo holder reward)

        var storageKey = new KeyBuilder(NativeContract.Ledger.Id, 12);
        snapshot.Add(storageKey, new StorageItem(new HashIndexState { Hash = UInt256.Zero, Index = persistingBlock.Index - 1 }));
        var keyCount = snapshot.GetChangeSet().Count();
        // Check unclaim

        var unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from, persistingBlock);
        Assert.AreEqual(new BigInteger(0.5 * 1000 * 100000000L), unclaim.Value);
        Assert.IsTrue(unclaim.State);

        // Transfer

        Assert.IsTrue(NativeContract.NEO.Transfer(snapshot, from, to, BigInteger.Zero, true, persistingBlock));
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = NativeContract.NEO.Transfer(snapshot, from, null, BigInteger.Zero, true, persistingBlock));
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = NativeContract.NEO.Transfer(snapshot, null, to, BigInteger.Zero, false, persistingBlock));
        Assert.AreEqual(100000000, NativeContract.NEO.BalanceOf(snapshot, from));
        Assert.AreEqual(0, NativeContract.NEO.BalanceOf(snapshot, to));

        Assert.AreEqual(52000500_00000000, NativeContract.TokenManagement.BalanceOf(snapshot, NativeContract.Governance.GasTokenId, new UInt160(from)));
        Assert.AreEqual(0, NativeContract.TokenManagement.BalanceOf(snapshot, NativeContract.Governance.GasTokenId, new UInt160(to)));

        // Check unclaim

        unclaim = UT_NeoToken.Check_UnclaimedGas(snapshot, from, persistingBlock);
        Assert.AreEqual(new BigInteger(0), unclaim.Value);
        Assert.IsTrue(unclaim.State);

        tokenInfo = NativeContract.TokenManagement.GetTokenInfo(snapshot, NativeContract.Governance.GasTokenId);
        supply = tokenInfo!.TotalSupply;
        Assert.AreEqual(5200050050000000, supply);

        Assert.AreEqual(keyCount + 3, snapshot.GetChangeSet().Count()); // Gas

        // Transfer

        keyCount = snapshot.GetChangeSet().Count();

        using (var engine1 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(), snapshot, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine1.LoadScript(Array.Empty<byte>());
            var result1 = await engine1.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new UInt160(from), new UInt160(to), 52000500_00000000, null);
            Assert.IsFalse(result1); // Not signed
        }

        using (var engine2 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(new UInt160(from)), snapshot, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine2.LoadScript(Array.Empty<byte>());
            var result2 = await engine2.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new UInt160(from), new UInt160(to), 52000500_00000001, null);
            Assert.IsFalse(result2); // More than balance
        }

        using (var engine3 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(new UInt160(from)), snapshot, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine3.LoadScript(Array.Empty<byte>());
            var result3 = await engine3.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new UInt160(from), new UInt160(to), 52000500_00000000, null);
            Assert.IsTrue(result3); // All balance
        }

        // Balance of

        Assert.AreEqual(52000500_00000000, NativeContract.TokenManagement.BalanceOf(snapshot, NativeContract.Governance.GasTokenId, new UInt160(to)));
        Assert.AreEqual(0, NativeContract.TokenManagement.BalanceOf(snapshot, NativeContract.Governance.GasTokenId, new UInt160(from)));

        Assert.AreEqual(keyCount + 1, snapshot.GetChangeSet().Count()); // All

        // Burn

        using var engine = ApplicationEngine.Create(TriggerType.Application, null, snapshot, persistingBlock, settings: TestProtocolSettings.Default, gas: 0);
        engine.LoadScript(Array.Empty<byte>());

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await engine.CallFromNativeContractAsync(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "burn", NativeContract.Governance.GasTokenId, new UInt160(to), BigInteger.MinusOne));

        // Burn more than expected

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await engine.CallFromNativeContractAsync(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "burn", NativeContract.Governance.GasTokenId, new UInt160(to), new BigInteger(52000500_00000001)));

        // Real burn

        await engine.CallFromNativeContractAsync(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "burn", NativeContract.Governance.GasTokenId, new UInt160(to), new BigInteger(1));

        Assert.AreEqual(5200049999999999, NativeContract.TokenManagement.BalanceOf(engine.SnapshotCache, NativeContract.Governance.GasTokenId, new UInt160(to)));

        Assert.AreEqual(2, engine.SnapshotCache.GetChangeSet().Count());

        // Burn all
        await engine.CallFromNativeContractAsync(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "burn", NativeContract.Governance.GasTokenId, new UInt160(to), new BigInteger(5200049999999999));

        Assert.AreEqual(keyCount - 2, engine.SnapshotCache.GetChangeSet().Count());

        // Bad inputs

        using (var engine4 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(new UInt160(from)), engine.SnapshotCache, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine4.LoadScript(Array.Empty<byte>());
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
                await engine4.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new UInt160(from), new UInt160(to), BigInteger.MinusOne, null));
        }

        using (var engine5 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(), engine.SnapshotCache, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine5.LoadScript(Array.Empty<byte>());
            Assert.ThrowsExactly<FormatException>(() => _ = engine5.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new byte[19], new UInt160(to), BigInteger.One, null));
        }

        using (var engine6 = ApplicationEngine.Create(TriggerType.Application, new Nep17NativeContractExtensions.ManualWitness(), engine.SnapshotCache, persistingBlock, settings: TestProtocolSettings.Default))
        {
            engine6.LoadScript(Array.Empty<byte>());
            Assert.ThrowsExactly<FormatException>(() => _ = engine6.CallFromNativeContractAsync<bool>(NativeContract.Governance.Hash, NativeContract.TokenManagement.Hash, "transfer", NativeContract.Governance.GasTokenId, new UInt160(from), new byte[19], BigInteger.One, null));
        }
    }

    internal static StorageKey CreateStorageKey(byte prefix, uint key)
    {
        return CreateStorageKey(prefix, BitConverter.GetBytes(key));
    }

    internal static StorageKey CreateStorageKey(byte prefix, byte[]? key = null)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(sizeof(byte) + (key?.Length ?? 0));
        buffer[0] = prefix;
        key?.CopyTo(buffer.AsSpan(1));
        return new()
        {
            Id = NativeContract.Governance.Id,
            Key = buffer
        };
    }
}
