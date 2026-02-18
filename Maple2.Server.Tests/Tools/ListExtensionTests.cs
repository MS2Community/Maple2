using System.Collections.Generic;
using System.Linq;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Tools;

public class ListExtensionTests {
    [Test]
    public void AddSorted_MaintainsAscendingOrder_WithRandomInputs() {
        var list = new List<int>();
        int[] inputs = [5, 1, 9, 3, 7, 2, 8, 4, 6, 0];

        foreach (int x in inputs) {
            list.AddSorted(x);
        }

        Assert.That(list, Is.EqualTo(inputs.OrderBy(x => x)));
    }

    [Test]
    public void AddSorted_InsertsAtBeginningAndEnd() {
        var list = new List<int>();
        list.AddSorted(10);
        list.AddSorted(20);
        list.AddSorted(0); // should go to beginning
        list.AddSorted(30); // should go to end

        Assert.Multiple(() => {
            Assert.That(list.First(), Is.EqualTo(0));
            Assert.That(list.Last(), Is.EqualTo(30));
            Assert.That(list, Is.Ordered);
        });
    }

    [Test]
    public void RemoveSorted_RemovesExisting_FirstAndLast() {
        var list = new List<int>();
        foreach (int x in Enumerable.Range(1, 10)) {
            list.AddSorted(x);
        }

        list.RemoveSorted(1); // first
        list.RemoveSorted(10); // last

        Assert.Multiple(() => {
            Assert.That(list, Does.Not.Contain(1));
            Assert.That(list, Does.Not.Contain(10));
            Assert.That(list, Is.Ordered);
        });
    }

    [Test]
    public void RemoveSorted_RemovesSingleInstance_WhenDuplicatesPresent() {
        var list = new List<int>();
        list.AddSorted(3);
        list.AddSorted(3);
        list.AddSorted(3);
        list.AddSorted(2);
        list.AddSorted(4);

        list.RemoveSorted(3);

        Assert.Multiple(() => {
            Assert.That(list.Count(x => x == 3), Is.EqualTo(2));
            Assert.That(list, Is.Ordered);
        });
    }

    [Test]
    public void RemoveSorted_DoesNothing_WhenItemNotPresent() {
        var list = new List<int>();
        int[] itemsToAdd = [1, 3, 5, 7];
        foreach (int x in itemsToAdd) {
            list.AddSorted(x);
        }

        int[] snapshot = list.ToArray();
        list.RemoveSorted(2);

        Assert.That(list, Is.EqualTo(snapshot));
        Assert.That(list, Is.Ordered);
    }

    [Test]
    public void AddRemoveSorted_WithCustomDescendingComparer() {
        var list = new List<int>();
        Comparer<int> desc = Comparer<int>.Create((a, b) => b.CompareTo(a));

        int[] itemsToAdd = [5, 1, 9, 3, 7];
        foreach (int x in itemsToAdd) {
            list.AddSorted(x, desc);
        }

        // verify strictly descending
        int[] expected = [9, 7, 5, 3, 1];
        Assert.That(list, Is.EqualTo(expected));

        // remove boundaries under the same comparer
        list.RemoveSorted(9, desc); // first (largest)
        list.RemoveSorted(1, desc); // last (smallest)

        Assert.That(list, Is.EqualTo(new[] {
            7,
            5,
            3,
        }));

        // remove middle
        list.RemoveSorted(5, desc);
        Assert.That(list, Is.EqualTo(new[] {
            7,
            3,
        }));
    }
}
