# AES-Driven IDO-Layer Data Replication

> [!CAUTION]
> **Project in pre-beta testing, and is being shared for collaboration purposes. Use at your own risk!**

An AES-driven IDO-layer data replication system for Syteline, that lets administrators set up event driven replication rules, external target systems, and even field-level data mapping/transformation.

## Status:

* IDOs structure - Complete
* Naming convention - Complete
* Main IDO Method - Framework Complete
* External Targets - Need to build out

## Tables

* ue_FDI_idorepl_cred_mst
* ue_FDI_idorepl_mapfield_mst
* ue_FDI_idorepl_mapfieldsource_mst
* ue_FDI_idorepl_rule_mst

## IDOs

* ue_FDI_IDOReplicationRules
* ue_FDI_IDOReplicationCredentials
* ue_FDI_IDOReplicationMapFields
* ue_FDI_IDOReplicationMapFieldSources

## Forms

* ue_FDI_IDOReplicationRules
![image](https://github.com/user-attachments/assets/78fe11a2-e623-480b-bd1d-1b44760298ea)
  
* ue_FDI_IDOReplicationCredentials
![image](https://github.com/user-attachments/assets/a05d732e-aeb0-48ef-9042-c01a54e5947e)

## External Targets:

* Azure EventHubs - Code Fully Written, But Waiting to Hear from Infor on Security Issues and/or Installing dependencies
* Salesforce REST API - Code Stub Written. Need to merge in code from other projects
* Other Syteline instances through REST or SOAP APIs - Conceptual, not started
* Direct SQL databases (PostGres, MS SQL Server, MySQL, etc) - Conceptual, not started
* Other ERP systems with a REST API - Conceptual, not started
