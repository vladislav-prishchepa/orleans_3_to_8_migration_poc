-- https://github.com/dotnet/orleans/blob/v3.7.2/src/AdoNet/Shared/MySQL-Main.sql

/*
Implementation notes:

1) The general idea is that data is read and written through Orleans specific queries.
   Orleans operates on column names and types when reading and on parameter names and types when writing.

2) The implementations *must* preserve input and output names and types. Orleans uses these parameters to reads query results by name and type.
   Vendor and deployment specific tuning is allowed and contributions are encouraged as long as the interface contract
   is maintained.

3) The implementation across vendor specific scripts *should* preserve the constraint names. This simplifies troubleshooting
   by virtue of uniform naming across concrete implementations.

5) ETag for Orleans is an opaque column that represents a unique version. The type of its actual implementation
   is not important as long as it represents a unique version. In this implementation we use integers for versioning

6) For the sake of being explicit and removing ambiguity, Orleans expects some queries to return either TRUE as >0 value
   or FALSE as =0 value. That is, affected rows or such does not matter. If an error is raised or an exception is thrown
   the query *must* ensure the entire transaction is rolled back and may either return FALSE or propagate the exception.
   Orleans handles exception as a failure and will retry.

7) The implementation follows the Extended Orleans membership protocol. For more information, see at:
        https://dotnet.github.io/orleans/Documentation/Runtime-Implementation-Details/Runtime-Tables.html
        https://dotnet.github.io/orleans/Documentation/Runtime-Implementation-Details/Cluster-Management.html
        https://github.com/dotnet/orleans/blob/master/src/Orleans.Core/SystemTargetInterfaces/IMembershipTable.cs
*/
-- This table defines Orleans operational queries. Orleans uses these to manage its operations,
-- these are the only queries Orleans issues to the database.
-- These can be redefined (e.g. to provide non-destructive updates) provided the stated interface principles hold.
CREATE TABLE OrleansQuery
(
    QueryKey VARCHAR(64) NOT NULL,
    QueryText VARCHAR(8000) NOT NULL,

    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);

-- -----------------------------------------------------------------------------------------------------------------------------------
-- -----------------------------------------------------------------------------------------------------------------------------------
-- -----------------------------------------------------------------------------------------------------------------------------------

-- https://github.com/dotnet/orleans/blob/v3.7.2/src/AdoNet/Orleans.Persistence.AdoNet/MySQL-Persistence.sql

-- The design criteria for this table are:
--
-- 1. It can contain arbitrary content serialized as binary, XML or JSON. These formats
-- are supported to allow one to take advantage of in-storage processing capabilities for
-- these types if required. This should not incur extra cost on storage.
--
-- 2. The table design should scale with the idea of tens or hundreds (or even more) types
-- of grains that may operate with even hundreds of thousands of grain IDs within each
-- type of a grain.
--
-- 3. The table and its associated operations should remain stable. There should not be
-- structural reason for unexpected delays in operations. It should be possible to also
-- insert data reasonably fast without resource contention.
--
-- 4. For reasons in 2. and 3., the index should be as narrow as possible so it fits well in
-- memory and should it require maintenance, isn't resource intensive. For this
-- reason the index is narrow by design (ideally non-clustered). Currently the entity
-- is recognized in the storage by the grain type and its ID, which are unique in Orleans silo.
-- The ID is the grain ID bytes (if string type UTF-8 bytes) and possible extension key as UTF-8
-- bytes concatenated with the ID and then hashed.
--
-- Reason for hashing: Database engines usually limit the length of the column sizes, which
-- would artificially limit the length of IDs or types. Even when within limitations, the
-- index would be thick and consume more memory.
--
-- In the current setup the ID and the type are hashed into two INT type instances, which
-- are made a compound index. When there are no collisions, the index can quickly locate
-- the unique row. Along with the hashed index values, the NVARCHAR(nnn) values are also
-- stored and they are used to prune hash collisions down to only one result row.
--
-- 5. The design leads to duplication in the storage. It is reasonable to assume there will
-- a low number of services with a given service ID operational at any given time. Or that
-- compared to the number of grain IDs, there are a fairly low number of different types of
-- grain. The catch is that were these data separated to another table, it would make INSERT
-- and UPDATE operations complicated and would require joins, temporary variables and additional
-- indexes or some combinations of them to make it work. It looks like fitting strategy
-- could be to use table compression.
--
-- 6. For the aforementioned reasons, grain state DELETE will set NULL to the data fields
-- and updates the Version number normally. This should alleviate the need for index or
-- statistics maintenance with the loss of some bytes of storage space. The table can be scrubbed
-- in a separate maintenance operation.
--
-- 7. In the storage operations queries the columns need to be in the exact same order
-- since the storage table operations support optionally streaming.
CREATE TABLE OrleansStorage
(
    -- These are for the book keeping. Orleans calculates
    -- these hashes (see RelationalStorageProvide implementation),
    -- which are signed 32 bit integers mapped to the *Hash fields.
    -- The mapping is done in the code. The
    -- *String columns contain the corresponding clear name fields.
    --
    -- If there are duplicates, they are resolved by using GrainIdN0,
    -- GrainIdN1, GrainIdExtensionString and GrainTypeString fields.
    -- It is assumed these would be rarely needed.
    GrainIdHash                INT NOT NULL,
    GrainIdN0                BIGINT NOT NULL,
    GrainIdN1                BIGINT NOT NULL,
    GrainTypeHash            INT NOT NULL,
    GrainTypeString            NVARCHAR(512) NOT NULL,
    GrainIdExtensionString    NVARCHAR(512) NULL,
    ServiceId                NVARCHAR(150) NOT NULL,

    -- The usage of the Payload records is exclusive in that
    -- only one should be populated at any given time and two others
    -- are NULL. The types are separated to advantage on special
    -- processing capabilities present on database engines (not all might
    -- have both JSON and XML types.
    --
    -- One is free to alter the size of these fields.
    PayloadBinary    BLOB NULL,
    PayloadXml        LONGTEXT NULL,
    PayloadJson        LONGTEXT NULL,

    -- Informational field, no other use.
    ModifiedOn DATETIME NOT NULL,

    -- The version of the stored payload.
    Version INT NULL

    -- The following would in principle be the primary key, but it would be too thick
    -- to be indexed, so the values are hashed and only collisions will be solved
    -- by using the fields. That is, after the indexed queries have pinpointed the right
    -- rows down to [0, n] relevant ones, n being the number of collided value pairs.
) ROW_FORMAT = COMPRESSED KEY_BLOCK_SIZE = 16;
ALTER TABLE OrleansStorage ADD INDEX IX_OrleansStorage (GrainIdHash, GrainTypeHash);

-- The following alters the column to JSON format if MySQL is at least of version 5.7.8.
-- See more at https://dev.mysql.com/doc/refman/5.7/en/json.html for JSON and
-- http://dev.mysql.com/doc/refman/5.7/en/comments.html for the syntax.

ALTER TABLE OrleansStorage MODIFY COLUMN PayloadJson JSON;

CREATE PROCEDURE ClearStorage
(
    in _GrainIdHash INT,
    in _GrainIdN0 BIGINT,
    in _GrainIdN1 BIGINT,
    in _GrainTypeHash INT,
    in _GrainTypeString NVARCHAR(512),
    in _GrainIdExtensionString NVARCHAR(512),
    in _ServiceId NVARCHAR(150),
    in _GrainStateVersion INT
)
BEGIN
    DECLARE _newGrainStateVersion INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    DECLARE EXIT HANDLER FOR SQLWARNING BEGIN ROLLBACK; RESIGNAL; END;

    SET _newGrainStateVersion = _GrainStateVersion;

    -- Default level is REPEATABLE READ and may cause Gap Lock issues
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;
    UPDATE OrleansStorage
    SET
        PayloadBinary = NULL,
        PayloadJson = NULL,
        PayloadXml = NULL,
        Version = Version + 1
    WHERE
        GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
        AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
        AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
        AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
        AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
        AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
        LIMIT 1;

    IF ROW_COUNT() > 0
    THEN
        SET _newGrainStateVersion = _GrainStateVersion + 1;
    END IF;

    SELECT _newGrainStateVersion AS NewGrainStateVersion;
    COMMIT;
END;

CREATE PROCEDURE WriteToStorage
(
    in _GrainIdHash INT,
    in _GrainIdN0 BIGINT,
    in _GrainIdN1 BIGINT,
    in _GrainTypeHash INT,
    in _GrainTypeString NVARCHAR(512),
    in _GrainIdExtensionString NVARCHAR(512),
    in _ServiceId NVARCHAR(150),
    in _GrainStateVersion INT,
    in _PayloadBinary BLOB,
    in _PayloadJson LONGTEXT,
    in _PayloadXml LONGTEXT
)
BEGIN
    DECLARE _newGrainStateVersion INT;
    DECLARE _rowCount INT;
    DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
    DECLARE EXIT HANDLER FOR SQLWARNING BEGIN ROLLBACK; RESIGNAL; END;

    SET _newGrainStateVersion = _GrainStateVersion;

    -- Default level is REPEATABLE READ and may cause Gap Lock issues
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    START TRANSACTION;

    -- Grain state is not null, so the state must have been read from the storage before.
    -- Let's try to update it.
    --
    -- When Orleans is running in normal, non-split state, there will
    -- be only one grain with the given ID and type combination only. This
    -- grain saves states mostly serially if Orleans guarantees are upheld. Even
    -- if not, the updates should work correctly due to version number.
    --
    -- In split brain situations there can be a situation where there are two or more
    -- grains with the given ID and type combination. When they try to INSERT
    -- concurrently, the table needs to be locked pessimistically before one of
    -- the grains gets @GrainStateVersion = 1 in return and the other grains will fail
    -- to update storage. The following arrangement is made to reduce locking in normal operation.
    --
    -- If the version number explicitly returned is still the same, Orleans interprets it so the update did not succeed
    -- and throws an InconsistentStateException.
    --
    -- See further information at https://dotnet.github.io/orleans/Documentation/Core-Features/Grain-Persistence.html.
    IF _GrainStateVersion IS NOT NULL
    THEN
        UPDATE OrleansStorage
        SET
            PayloadBinary = _PayloadBinary,
            PayloadJson = _PayloadJson,
            PayloadXml = _PayloadXml,
            ModifiedOn = UTC_TIMESTAMP(),
            Version = Version + 1
        WHERE
            GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
            AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
            AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
            AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
            AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
            AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
            AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
            AND Version IS NOT NULL AND Version = _GrainStateVersion AND _GrainStateVersion IS NOT NULL
            LIMIT 1;

        IF ROW_COUNT() > 0
        THEN
            SET _newGrainStateVersion = _GrainStateVersion + 1;
            SET _GrainStateVersion = _newGrainStateVersion;
        END IF;
    END IF;

    -- The grain state has not been read. The following locks rather pessimistically
    -- to ensure only on INSERT succeeds.
    IF _GrainStateVersion IS NULL
    THEN
        INSERT INTO OrleansStorage
        (
            GrainIdHash,
            GrainIdN0,
            GrainIdN1,
            GrainTypeHash,
            GrainTypeString,
            GrainIdExtensionString,
            ServiceId,
            PayloadBinary,
            PayloadJson,
            PayloadXml,
            ModifiedOn,
            Version
        )
        SELECT * FROM ( SELECT
            _GrainIdHash,
            _GrainIdN0,
            _GrainIdN1,
            _GrainTypeHash,
            _GrainTypeString,
            _GrainIdExtensionString,
            _ServiceId,
            _PayloadBinary,
            _PayloadJson,
            _PayloadXml,
            UTC_TIMESTAMP(),
            1) AS TMP
        WHERE NOT EXISTS
        (
            -- There should not be any version of this grain state.
            SELECT 1
            FROM OrleansStorage
            WHERE
                GrainIdHash = _GrainIdHash AND _GrainIdHash IS NOT NULL
                AND GrainTypeHash = _GrainTypeHash AND _GrainTypeHash IS NOT NULL
                AND GrainIdN0 = _GrainIdN0 AND _GrainIdN0 IS NOT NULL
                AND GrainIdN1 = _GrainIdN1 AND _GrainIdN1 IS NOT NULL
                AND GrainTypeString = _GrainTypeString AND _GrainTypeString IS NOT NULL
                AND ((_GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = _GrainIdExtensionString) OR _GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
                AND ServiceId = _ServiceId AND _ServiceId IS NOT NULL
        ) LIMIT 1;

        IF ROW_COUNT() > 0
        THEN
            SET _newGrainStateVersion = 1;
        END IF;
    END IF;

    SELECT _newGrainStateVersion AS NewGrainStateVersion;
    COMMIT;
END;

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ReadFromStorageKey',
    'SELECT
        PayloadBinary,
        PayloadXml,
        PayloadJson,
        UTC_TIMESTAMP(),
        Version
    FROM
        OrleansStorage
    WHERE
        GrainIdHash = @GrainIdHash
        AND GrainTypeHash = @GrainTypeHash AND @GrainTypeHash IS NOT NULL
        AND GrainIdN0 = @GrainIdN0 AND @GrainIdN0 IS NOT NULL
        AND GrainIdN1 = @GrainIdN1 AND @GrainIdN1 IS NOT NULL
        AND GrainTypeString = @GrainTypeString AND GrainTypeString IS NOT NULL
        AND ((@GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString IS NOT NULL AND GrainIdExtensionString = @GrainIdExtensionString) OR @GrainIdExtensionString IS NULL AND GrainIdExtensionString IS NULL)
        AND ServiceId = @ServiceId AND @ServiceId IS NOT NULL
        LIMIT 1;'
);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'WriteToStorageKey','
    call WriteToStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion, @PayloadBinary, @PayloadJson, @PayloadXml);'
);

INSERT INTO OrleansQuery(QueryKey, QueryText)
VALUES
(
    'ClearStorageKey','
    call ClearStorage(@GrainIdHash, @GrainIdN0, @GrainIdN1, @GrainTypeHash, @GrainTypeString, @GrainIdExtensionString, @ServiceId, @GrainStateVersion);'
);
