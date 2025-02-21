# CollectionsMarshal Support For Unity
The `CollectionsMarshal` class is introduced in .Net5. Its functionality allows you to get references to the internal values of `List<T>` and `Dictionary<TKey, TValue>`. This is particularly useful when T and TValue are value types (e.g., struct), as it helps avoid unnecessary copying.

## Import

Simply add CollectionsMarshal.cs to your project.



## Dependence

Depends on the `System.Runtime.CompilerServices.Unsafe` class, which can be downloaded from NuGet: https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe



## Announce
This project is modified based on the implementation of CollectionsMarshal in .net9. It has only been tested in Unity Editor 2022.3. Issues are welcome.



## Usage
Here is a simple example:

>   Versions below C#13.0 **cannot use** ref and unsafe in asynchronous methods or iterator methods, so CollectionsMarshal cannot be used in these methods either.

```cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

public struct Test
{
    public int A;

    public static implicit operator Test(int value)
    {
        return new Test { A = value };
    }
}

public sealed partial class CollectionsMarshalTest : MonoBehaviour
{
    void Start()
    {
        var ls = new List<Test> {1, 2, 3, 4, 5};

        var span = CollectionsMarshal.AsSpan(ls);
        span[0].A = 100;
        Debug.Assert(ls[0].A == 100);
        span[3].A = 400;
        Debug.Assert(ls[3].A == 400);
        
        CollectionsMarshal.SetCount(ls, 6);
        Debug.Assert(ls.Count == 6);
        Debug.Assert(ls[5].A == 0);

        var d = new Dictionary<int, Test> {{1, 1}, {2, 2}, {3, 3}, {4, 4}, {5, 5}};
        ref var val = ref CollectionsMarshal.GetValueRefOrNullRef(d, 1);
        val.A = 100;
        Debug.Assert(d[1].A == 100);

        val = ref CollectionsMarshal.GetValueRefOrNullRef(d, 6);
        Debug.Assert(Unsafe.IsNullRef(ref val));

        ref var val2 = ref CollectionsMarshal.GetValueRefOrAddDefault(d, 6, out var exists);
        Debug.Assert(exists == false);
        Debug.Assert(d[6].A == 0);
        val2.A = 200;
        Debug.Assert(d[6].A == 200);

        Debug.Log("Done");
    }
}
```

