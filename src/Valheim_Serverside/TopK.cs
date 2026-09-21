using System;
using System.Collections.Generic;

namespace Valheim_Serverside
{
	// Bounded-heap partial sort: first min(k,n) elements become the k lowest-ranked items
	// in ascending order. O(n log k), main-thread only, grows static scratch as needed.
	internal static class TopK
	{
		private static double[] ranks;
		private static int[] heap;
		private static bool[] taken;
		private static object[] buf;

		internal static void Select<T>(List<T> items, Func<T, double> rank, int k)
		{
			int n = items.Count;
			if (n == 0 || k <= 0) return;
			if (k > n) k = n;

			if (ranks == null || ranks.Length < n)
			{
				ranks = new double[n];
				heap = new int[n];
				taken = new bool[n];
			}
			if (buf == null || buf.Length < n) buf = new object[n];

			for (int i = 0; i < n; i++)
			{
				buf[i] = items[i];
				ranks[i] = rank(items[i]);
				taken[i] = false;
			}

			int heapCount = 0;
			for (int i = 0; i < n; i++)
			{
				if (heapCount < k)
				{
					heap[heapCount] = i;
					heapCount++;
					SiftUp(heapCount - 1);
				}
				else if (ranks[i] < ranks[heap[0]])
				{
					heap[0] = i;
					SiftDown(0, heapCount);
				}
			}

			int count = heapCount;
			for (int pos = count - 1; pos >= 0; pos--)
			{
				int idx = heap[0];
				taken[idx] = true;
				items[pos] = (T)buf[idx];
				heapCount--;
				if (heapCount > 0)
				{
					heap[0] = heap[heapCount];
					SiftDown(0, heapCount);
				}
			}

			int w = count;
			for (int i = 0; i < n; i++)
			{
				if (!taken[i]) items[w++] = (T)buf[i];
			}
		}

		private static void SiftUp(int i)
		{
			while (i > 0)
			{
				int parent = (i - 1) / 2;
				if (ranks[heap[parent]] >= ranks[heap[i]]) break;
				int tmp = heap[parent];
				heap[parent] = heap[i];
				heap[i] = tmp;
				i = parent;
			}
		}

		private static void SiftDown(int i, int count)
		{
			while (true)
			{
				int left = 2 * i + 1, right = 2 * i + 2, largest = i;
				if (left < count && ranks[heap[left]] > ranks[heap[largest]]) largest = left;
				if (right < count && ranks[heap[right]] > ranks[heap[largest]]) largest = right;
				if (largest == i) break;
				int tmp = heap[i];
				heap[i] = heap[largest];
				heap[largest] = tmp;
				i = largest;
			}
		}
	}
}
