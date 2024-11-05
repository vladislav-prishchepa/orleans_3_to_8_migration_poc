# Orleans v3 -> v8 migration PoC

This repository demonstrates how to migrate Orleans v3 to Orleans v8 without persistent grain state loss and complex data migration, with a reasonable cluster downtime.

The migration process depends very much on your cluster Orleans configuration. During migration the following conditions are set:

* Orleans v3 stores grain state using JSON column, not only for better readability and backward compatibility, but also for indexing support
* Orleans v8 will also use the same JSON column for the same reasons
* no grain state data migration - for reasonable downtime and backward compatibility if Orleans v8 -> Orleans v3 downgrade required

The PoC is available for MySQL (via MySqlConnector) and PostgreSQL. MS SQL based clusters can also be migrated using a similar approach.

How to run example:

* set preferred `Invariant` and `ConnectionString` in `Orleans3App` config
* run `Orleans3App` project, it will initialize database, initialize multiple grains with different key types and persist their state, after that the app will be shuted down. On each startup the app will run *Orleans query and procedures* migration ti make sure they are compatible with Orleans v3.
* set **the same** `Invariant` and `ConnectionString` in `Orleans8App` config
* run `Orleans8App`, it will check all existing (Orleans v3-initialized) grain instances and also create and persist several new grain . On each startup the app will run *Orleans query and procedures* migration ti make sure they are compatible with Orleans v8.
* you can also run `Orleans3App` after `Orleans8App`, all existing grains must be operational

## Implementation details

### HashPicker

Orleans v7 introduced a breaking change in grain identity hashing - algorithm was changed and also grain id serialization was changed for `IGrainWithStringKey` grains. A custom `IHashPicker` implementation is used to handle it. See [correlating issue](https://github.com/dotnet/orleans/issues/9141).


### Serialized state transfer

Orleans v8 always reads/writes serialized state as `byte[]`, so we need to convert it to/from json/jsonb. This PoC performs it at Orleans query level - see `before_run.sql` scripts in `Orleans8App`, they are slightly different to `Orleans3App`.

### JSON serialization settings

By default Orleans uses `TypeNameHandling` to annotate .NET Types in JSON. This annotations includes full type names which are likely to change during the migration process. So, `TypeNameHandling` is set to `None` to handle such changes.

### Grain types annotations

`[PersistentStateAttribute]` state names must be changed.

For example, Orleans v3 grain

```c#

namespace Orleans3App.Grains;

public class TestStringKeyGrain : TestGrainBase, ITestStringKeyGrain
{
    public TestStringKeyGrain(
        [PersistentState("TestStringKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestStringKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
```

Must be transformed into this for Orleans v8:

```c#
namespace Orleans8App.Grains;

public class TestStringKeyGrain : TestGrainBase, ITestStringKeyGrain
{
    public TestStringKeyGrain(
        // set legacy GrainTypeString
        [PersistentState("Orleans3App.Grains.TestStringKeyGrain,Orleans3App.TestStringKeyGrainState")]
        IPersistentState<TestGrainState> persistentState,
        ILogger<TestStringKeyGrain> logger)
        : base(persistentState, logger)
    {
    }
}
```

You can check legacy state names in `GrainTypeString` column in your database.
