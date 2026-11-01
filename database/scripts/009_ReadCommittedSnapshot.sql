-- 009_ReadCommittedSnapshot.sql – readers no longer wait for writers (S31, ADR-041).
--   With READ_COMMITTED_SNAPSHOT a query reads the last committed version of a row instead of waiting for the lock
--   of a transaction that is changing it. Dashboards, lists and reports keep answering while an import or a carry
--   forward runs (seen as a 31 s wait in the analytics test). Writers still lock each other, so the rowversion checks
--   (409), the atomic pick and the issue-numbering app lock are unchanged.
--   ALTER DATABASE cannot run inside a transaction and needs the database to itself: ROLLBACK IMMEDIATE ends other
--   sessions (stop the API before applying this script).

ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
