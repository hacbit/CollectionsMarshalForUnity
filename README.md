# CollectionsMarshal Support For Unity

`CollectionsMarshal` class 是 .Net5 开始引入的类，其功能简单来说可以获取 `List<T>` 和 `Dictionary<TKey, TValue>` 内部的值的引用，对于 `T` 和 `TValue` 是值类型（比如 struct） 的时候就非常好用，这会让你避免额外的复制。

## Import

直接把 CollectionsMarshal.cs 放进项目中即可



## Dependence

依赖 `System.Runtime.CompilerServices.Unsafe` class，对于这个类，可以从 nuget 上下载对应的 nuget 包： https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe



## Announce

本项目是参考 CollectionsMarshal 在 .net9 中的实现，修改而成的，虽然经过了一定的基准测试，但是毕竟涉及了一些 unsafe 的操作，**不保证其安全性，也不保证不会有 bug （欢迎提 issue）**

目前只测试了 Unity Editor 2022.3



## Usage

以下是一个简单的示例

>   C#13.0 以下版本**无法在异步方法或迭代器方法中使用** ref 和 unsafe，因此也无法在这些方法中使用 CollectionsMarshal

```cs
using System;
using System.Collections.Generic;
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



