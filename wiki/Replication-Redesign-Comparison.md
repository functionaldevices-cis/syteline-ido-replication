# Replication Redesign: Comparing the Options

**Status: analysis.** Nothing on this page is built. It compares the two ways replication could find out about changes, weighs risk, complexity and how quickly a change reaches the target, and recommends one. The evidence comes from the sandbox tests in [Trigger Diagnostics](Trigger-Diagnostics) and from reading Infor's DLLs in `Lib/`. Each point says which.

## What the two options share

Both options are the [AES plan](Replication-Redesign-AES-Plan) with a different first step. Everything after a change is captured is the same in both: the change queue, the doorbell, the one sender, the rules that merge queued operations, Depends On, and the sweep. That is most of the work. They differ only in **how a change is captured inside the save**:

- **AES capture**, as the AES plan describes it: event handlers on the replicated IDO write a queue row by key, or by `RowPointer` when the caller sent one.
- **Extending IDO capture**: AIR creates an IDO that extends and replaces the top of the replicated IDO's replace chain. Its extension class handles the runtime's own item events (`IIDOEventSource` in the DLLs, given to an extension class through `SetContext`) and writes the same queue row. Per the DLLs, a save to the base IDO loads the top replacer and every IDO it inherits from, with their extension classes, so ours runs on every save to the base for as long as it is in that chain ([Replace chains in the DLLs](Trigger-Diagnostics#replace-chains-in-the-dlls)).

Because the doorbell and sender are the same, so is the typical time to reach the target.

## Side by side

| | AES capture | Extending IDO capture |
| --- | --- | --- |
| **Typical sync time** (a form save, or a REST or extension-class save that sends the keys) | About 1 to 7 seconds after the save. A burst of 20 saves cleared about 15 seconds after the last ([doorbell](Trigger-Diagnostics#doorbell-results) and [burst](Trigger-Diagnostics#burst-results) results) | The same: same doorbell and sender |
| **Cost added to each save** | One IDO method call per record per event | A .NET call in the same process, most likely less (not measured) |
| **REST update by ItemId** | Not seen: `P()` was blank in every test. Sent when the sweep finds it, minutes later | Most likely seen at once: an ItemID holds the row's RowPointer (`alias.ID=[guid]`, parsed by `UpdateItemID` in the DLLs), and the item event carries the update item (`IDOItemUpdateEventArgs.UpdateItem`). Not tested |
| **REST delete by ItemId** | **Not sent.** The sweep can't find deletes | Most likely seen at once, as above. Not tested |
| **The order header update that follows a line save** | Not seen. Waits for the sweep, or a rule that queues the parent | Most likely seen, if that update carries an ItemID. Not tested |
| **Changes made by stored procedures, children deleted with their parent** | Not seen. Updates wait for the sweep; child deletes are sent only through Depends On | The same |
| **Side IDOs** (`SLCos_FT`, `ZIT_*`, portal IDOs) | Not tested | Per the DLLs, a save through a side IDO doesn't load the base's replacer, so each side IDO would need its own extending IDO |
| **Someone adds another IDO that replaces the same base** | No effect | **Ours drops out of the chain without an error**, and which replacer wins isn't defined. Every change to that IDO is missed, deletes included, until it's found and repaired |
| **Turning it off** | Deactivate the handlers on the Configs form | Take our IDO out of the chain: an import, then a metadata refresh that reaches every server |
| **Right after an assembly upload** | Saves can fail. Deactivate the triggers before uploading | Saves can fail, and there's no quick way to turn it off, because every save to the base loads our class |
| **Infor updates and other add-ons** | Event handlers stand on their own | Shares the replace chain with every other customization and partner add-on |
| **How much is proven** | Tested in a cloud sandbox | Only from reading the DLLs: the in-process events, what an ItemID update shows them, side IDOs and repointing are all untested |

## AES capture

**For it**

- Nothing outside AIR can switch it off without anyone noticing.
- Most of it is built and tested: the trigger setup, Fix Triggers, the status on the Configs form, and activating and deactivating.
- An administrator needs to know nothing about replace chains, which matters for an open-source project meant for administrators who aren't developers.
- When it misses a change, the miss is narrow and shows: one kind of caller, logged as an unidentified or `Skipped` row.

**Against it**

- **REST callers that work by ItemId.** Loading records, taking each one's `_ItemId` and updating with it is the usual REST pattern. Those updates reach the target only as often as the sweep runs, and the sweep is slow on a large table (about 2 to 9 seconds per poll on `SLLedgers`, the [polling cost test](Trigger-Diagnostics#polling-results)). Deletes by ItemId don't reach the target at all without a reconciliation job.

## Extending IDO capture

**For it**

- It most likely closes the ItemId gap, deletes included. That is the biggest difference in what each option can get right.
- It most likely sees the follow-on updates, such as the order header.
- It most likely adds less to each save.

**Against it**

- **Its worst case is worse than anything in the AES option.** An administrator creating an ordinary extend-and-replace IDO, or an add-on that installs one, can cut ours out of the chain without an error. Only a check that reads `IdoCollections` can find that, because check-in and import fire no events ([IDO definition test](Trigger-Diagnostics#the-ido-definition-test)). The repair is an import and a metadata refresh, and a busy server can keep the old chain until the refresh reaches it. Deletes in that time are lost.
- **It is the more complex option.** It needs everything in the AES option, plus:
  - an IDO created and kept up to date per replicated IDO, and per side IDO
  - the chain check, and repointing our IDO when it fails
  - a way to turn it off, and a way to uninstall it
- It relies on an in-process event API known only from the DLLs.
- It makes AIR part of every save to the base IDO, so a bad assembly upload affects more.

## Recommendation

1. **Build the shared parts with AES capture first:** the queue, the doorbell, the sender, the merge rules, Depends On and the sweep. They're needed whichever capture is used. That gives changes within seconds for form saves and for REST and extension-class saves that send the keys.
2. **Find out how big the ItemId gap is.** Check whether the integrations in use, and Infor's own ION flows, update or delete by `_ItemId`. If they don't, the AES option's limits are only those the [AES plan](Replication-Redesign-AES-Plan#known-limits) already lists.
3. **If it matters, run two tests before building extending IDO capture:**
   - Does an extension class on an extending IDO get the in-process item events, and see the record's RowPointer, for a REST update and delete by ItemId?
   - Does Mongoose refuse a second IDO that replaces a base another IDO already replaces? `IdoCollections` has an `IdoCollectionExtendsValidSp` method, which may check this. If it does, the main risk above mostly goes away.
4. **If both answers are yes, add extending IDO capture as an option per IDO, alongside the AES triggers, not in place of them.** Both write to the same queue, and the merge rules absorb the duplicates. If our IDO ever drops out of the chain, the AES triggers still capture form saves and saves that send the keys.
