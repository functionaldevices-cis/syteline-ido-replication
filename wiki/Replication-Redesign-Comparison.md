# Replication Redesign: Comparing the Options

**Status: analysis.** Nothing on this page is built. It compares two ways replication could find out about changes, AES event handlers and an extending IDO, weighs risk, complexity and how quickly a change reaches the target, and recommends one. A third way, Syteline's own SQL replication triggers, was tested and is ruled out ([below](#a-third-option-sql-replication-triggers)). The evidence comes from the sandbox tests in [Trigger Diagnostics](Trigger-Diagnostics) and from reading Infor's DLLs in `Lib/`. A point marked *DLLs* was read from the DLLs and not tested, one marked *tested* was seen in a cloud sandbox, and one marked *design* follows from how the option would be built.

## What the two options share

Both options are the [AES plan](Replication-Redesign-AES-Plan) with a different first step. Everything after a change is captured is the same in both: the change queue, the doorbell, the one sender, the rules that merge queued operations, Depends On, and the sweep. That is most of the work. They differ only in **how a change is captured inside the save**:

- **AES capture**, as the AES plan describes it: event handlers on the replicated IDO write a queue row by key, or by `RowPointer` when the caller sent one.
- **Extending IDO capture**: AIR creates an IDO that extends and replaces the top of the replicated IDO's replace chain. Its extension class handles the runtime's own item events and writes the same queue row. An extension class gets those events through `SetContext`: its context's IDO is an `IIDOEventSource`, with events such as `PostItemInsert` *(DLLs)*. A save to the base IDO loads the top replacer and every IDO it inherits from, with their extension classes, so ours runs on every request to the base for as long as it is in that chain ([Replace chains in the DLLs](Trigger-Diagnostics#replace-chains-in-the-dlls)).

In both, the doorbell is an AES handler with Ignore Failure off, as the AES plan describes, unless the extending option rings it from its extension class instead. If it stays an AES handler, the extending option carries the AES option's risks around that handler as well as its own.

A **side IDO** below is an IDO that extends a base without replacing it, such as `SLCos_FT` beside `SLCos`. A save through it is a save to the same tables under another IDO name.

## Side by side

| | AES capture | Extending IDO capture |
| --- | --- | --- |
| **Typical time to the sender** (a form save, or a REST or extension-class save that sends the keys) | The sender's code started about 1 to 7 seconds after the save, and a burst of 20 saves plus one long save cleared about 15 seconds after the last *(tested, with the doorbell on `IdoOnUpdateCollection`, not the `IdoPostUpdateCollection` the plan uses; [doorbell](Trigger-Diagnostics#doorbell-results) and [burst](Trigger-Diagnostics#burst-results) results)*. Sending to the target comes on top | The same *(design: same doorbell and sender)* |
| **Cost added to each save** | Not measured. One IDO method call per record per event, plus the queue row | Not measured. A .NET call in the same process, plus the same queue row, which most likely costs the most in both |
| **REST update by ItemId** | Not seen: `P()` was blank in every test *(tested)*. Sent when the sweep finds it, minutes later | Most likely seen at once: an ItemID holds the row's RowPointer (`alias.ID=[guid]`, parsed by `UpdateItemID`), and the item event carries the update item (`IDOItemUpdateEventArgs.UpdateItem`) *(DLLs)* |
| **REST delete by ItemId** | **Not sent.** The sweep can't find deletes *(tested that `P()` is blank)* | Most likely seen at once, as above *(DLLs)* |
| **The order header update that follows a line save** | Not seen *(tested)*. Waits for the sweep, or a rule that queues the parent | Seen if that update carries an ItemID *(DLLs, not checked for this update)* |
| **A save made by another event handler** | AES handlers don't fire for a request whose initiator is an event handler (`VirtualIDO.AnyHandlersForFrameworkEvent`) *(DLLs)*. Waits for the sweep, and a delete is lost | In-process events still fire *(DLLs)* |
| **Children inserted as a nested update of their parent** (an extension class that saves an order with its lines in one request) | Not seen: the line IDO fired no events, and the lines didn't exist yet during the header's events *(tested once, [F7](Trigger-Diagnostics#results-of-the-last-tests))*. Sent when a rule queues the parent's children, or by the sweep | Not known: whether the child's extension class events fire in a nested save wasn't checked |
| **Changes made by stored procedures, children deleted with their parent** | Not seen *(tested for child deletes)*. Updates wait for the sweep; child deletes are sent only through Depends On | The same |
| **Side IDOs** | Most likely needs handlers on each side IDO too: handlers are found by the IDO's name *(DLLs, not tested)* | Needs its own extending IDO on each side IDO: a save through a side IDO never reaches the base's replacer *(DLLs)* |
| **Someone adds another IDO that replaces the same base** | Most likely no effect. The base's handlers fired in every test on `SLEndtypes`, which another IDO already replaces in that sandbox, so the event system matched them through one level of replacing *(tested indirectly)* | **One of the two replacers drops out of the chain without an error** *(DLLs)*. If it's ours, every change to that IDO is missed, deletes included; if it's the other, someone else's customization stops working. Which one wins isn't defined, and can differ between app domains, so misses can come and go between servers |
| **Turning it off** | Deactivate the handlers on the Configs form | Take our IDO out of the chain: an import, then a metadata refresh that reaches every server |
| **Right after an assembly upload** | Saves can fail *(tested)*. Deactivate the triggers before uploading | Every load, save and method call on the base IDO creates our class (`CommandRunner.InitExtensionClass`), so one that can't be loaded fails all of them, and there's no quick way to turn it off *(DLLs)*. An assembly the runtime can't find in its cache is skipped without an error, which would turn capture off without anyone knowing *(DLLs)* |
| **If the IDO ours extends goes away** | Doesn't apply | A replacing IDO whose base is missing breaks IDO metadata on every app domain that reads the list, for every IDO, until fixed *(DLLs)*. Ours extends the top of someone else's chain, so deleting or renaming that IDO, or importing ours before it exists, would do this |
| **Infor updates and other add-ons** | Event handlers stand on their own | Shares the replace chain with every other customization and partner add-on |
| **How much is proven** | The event behavior it relies on was tested. The queue row in the save, the doorbell on `IdoPostUpdateCollection`, and the transaction on the other events weren't ([Still to test](Replication-Redesign-AES-Plan#still-to-test)) | The capture itself is known only from the DLLs: the in-process events, what they show for an ItemId caller, whether their queue writes join the save's transaction, side IDOs and repointing are all untested |

## AES capture

**For it**

- Other customizations can't cut it out. Turning it off takes deactivating its handlers, which the Configs form shows.
- Most of it is built and tested: the trigger setup, Fix Triggers, the status on the Configs form, and activating and deactivating.
- An administrator needs to know nothing about replace chains, which matters for an open-source project meant for administrators who aren't developers.
- What it misses is known in advance: ItemId callers, saves made by other event handlers, stored procedures and cascade deletes. A trigger that fires but can't name the record leaves a log row; the others leave none.

**Against it**

- **REST callers that work by ItemId.** Loading records, taking each one's `_ItemId` and updating with it is a common REST pattern (the test script's `-Identify ItemId` does it). Those updates reach the target only as often as the sweep runs, and the sweep is slow on a large table (about 2 to 9 seconds per poll on `SLLedgers`, the [polling cost test](Trigger-Diagnostics#polling-results)). Deletes by ItemId don't reach the target at all without a reconciliation job.
- **Saves made by other event handlers** most likely aren't seen either *(DLLs)*.

## Extending IDO capture

**For it**

- It most likely closes the ItemId gap, deletes included. That is the biggest difference in what each option can get right.
- It most likely sees saves made by other event handlers, and follow-on updates such as the order header.

**Against it**

- **Its worst cases are worse than anything in the AES option.** An administrator creating an ordinary extend-and-replace IDO, or an add-on that installs one, can cut ours or theirs out of the chain without an error. Only a check that reads `IdoCollections` can find that, because check-in and import fire no events ([IDO definition test](Trigger-Diagnostics#the-ido-definition-test)). The repair is an import and a metadata refresh, and a busy server can keep the old chain until the refresh reaches it. Deletes in that time are lost. If the IDO ours extends is deleted or renamed, IDO metadata breaks on the server for every IDO.
- **It is the more complex option.** It needs everything in the AES option, plus:
  - an IDO created and kept up to date per replicated IDO, and per side IDO
  - the chain check, and repointing our IDO when it fails
  - a way to turn it off, and a way to uninstall it
- Its event handlers have no Ignore Failure setting, so an exception in one fails the user's save. They must never throw.
- It relies on an in-process event API known here only from the DLLs. Infor's Mongoose developer documentation may describe it; that hasn't been checked.
- It makes AIR part of every request to the base IDO, loads included, so a bad assembly upload affects more.

## A third option: SQL replication triggers

Syteline's own replication framework captures changes with SQL triggers on the tables, generated from replication categories and rules, and Infor's table feed to Data Lake is set up with it. It was tested in a sandbox ([The SQL replication test](Trigger-Diagnostics#the-sql-replication-test)). A point marked *production* was seen read-only in a production tenant.

**For it**

- SQL triggers fire on every write to the table, so they would see ItemId callers, stored procedures, cascade deletes and saves made by other event handlers *(design; only form saves were tried)*.
- Its messages carry the whole row, and a delete carries the table's keys *(tested)*.
- It is set up with Syteline's own forms, with no SQL access *(tested)*.

**Against it**

- **Replication can't read the changes.** The framework's own sender took the change from the queue within seconds, even with a rule set to send every 365 days, and tried to send it to the target site *(tested once, with the IDO XML transfer type)*. With the Data Lake transfer type, nothing appeared in the tables checked *(tested)*. Replication doesn't send through ION or act as a Syteline site.
- **Changing it means a trigger regenerate.** The Replication Management form has no choice of category or table *(tested)*, so it most likely rebuilds triggers that Infor's BOD publishing and multi-site replication depend on *(not checked)*. Its log in production had lock timeouts and deadlocks on three days *(production)*.
- **It held up a save.** The first save after a regenerate took about 15 seconds longer *(tested once)*.
- **A feed built on it stopped unnoticed.** In production, the Data Lake feed's newest data was from when the tenant moved to new servers, and nothing depended on it, so nobody noticed for months *(production)*.
- Changes are per table, not per IDO record, so replication would have to trace each table change back to the IDO's record *(design)*.

So the SQL path is ruled out as a way to capture changes. Its one lesson for the other two options: a feed can stop without anyone noticing, so whichever is built should check its own triggers and warn when a rule that usually sees changes goes quiet.

## Recommendation

1. **Build the shared parts with AES capture first:** the queue, the doorbell, the sender, the merge rules, Depends On and the sweep. They're needed whichever capture is used. That gives changes within seconds for form saves and for REST and extension-class saves that send the keys.
2. **Find out how big the gaps are.** Check whether the integrations in use, and Infor's own ION flows, update or delete by `_ItemId`, and whether the replicated IDOs are saved by other event handlers. If not, the AES option's limits are only those the [AES plan](Replication-Redesign-AES-Plan#known-limits) already lists.
3. **Add a health check, whichever capture is built:** confirm the handlers (or IDOs) it depends on are still in place, and warn when a rule that usually sees changes has been quiet for longer than usual.
4. **If the gaps matter, test before building extending IDO capture:**
   - Does an extension class on an extending IDO get the in-process item events, and see the record's RowPointer, for a REST update and delete by ItemId?
   - Do its queue writes roll back with a failed save?
   - Does Mongoose refuse a second IDO that replaces a base another IDO already replaces? `IdoCollections` has an `IdoCollectionExtendsValidSp` method, which may check this. If it does, the main risk above mostly goes away.
5. **If the answers are yes, consider extending IDO capture as an option per IDO, alongside the AES triggers, not in place of them.** If our IDO ever drops out of the chain, the AES triggers still capture form saves and saves that send the keys. That needs more of the merge rules than the AES plan has: rows for the same operation captured twice (Insert and Insert, Update and Insert, Delete and Delete), and both captures naming the record the same way, by RowPointer or by keys. Running both also gives up the lower cost per save.
