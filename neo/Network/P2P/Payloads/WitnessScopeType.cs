﻿namespace Neo.Network.P2P.Payloads
{
    public enum WitnessScopeType : byte
    {
        /// <summary>
        /// (neo2) - no params
        /// </summary>
        Global = 0x00,

        /// <summary>
        /// RootAccess means that this condition must hold: EntryScriptHash == CallingScriptHash
        /// No params is needed, as the witness/permission/signature given on root of invocation will automatically expire if entering deeper internal invokes
        /// This can be default safe choice for native NEO/GAS (previously used on Neo 2 as "attach" mode)
        /// </summary>
        RootAccess = 0x01,

        /// <summary>
        /// Custom hash for contract-specific
        /// </summary>
        CustomScriptHash = 0x02,

        /// <summary>
        ///  Custom pubkey for group members
        /// </summary>
        ExecutingGroupPubKey = 0x03
    }
}