using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    // Summary: Provides a set of static (Shared in Visual Basic) methods for querying objects
    //          that implement System.Collections.Generic.IEnumerable<T>.
    internal static class EnumerableExtensions
    {
        public static TSource FirstOrDefaultChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.FirstOrDefault() : default(TSource); return ret; }
        public static TSource FirstOrDefaultChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.FirstOrDefault(predicate) : default(TSource); return ret; }
        public static TSource LastOrDefaultChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.LastOrDefault() : default(TSource); return ret; }
        public static TSource LastOrDefaultChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.LastOrDefault(predicate) : default(TSource); return ret; }

        public static bool AllChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.All(predicate) : true; return ret; }
        public static bool AnyChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.Any() : false; return ret; }
        public static bool AnyChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.Any(predicate) : false; return ret; }

        public static int CountChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.Count() : 0; return ret; }
        public static int CountChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.Count(predicate) : 0; return ret; }

        public static IEnumerable<TSource> DefaultIfEmptyChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.DefaultIfEmpty() : null; return ret; }
        public static IEnumerable<TSource> DefaultIfEmptyChecked<TSource>(this IEnumerable<TSource> source, TSource defaultValue) { var ret = source != null ? source.DefaultIfEmpty(defaultValue) : null; return ret; }

        public static IEnumerable<TSource> DistinctChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.Distinct() : null; return ret; }
        public static IEnumerable<TSource> DistinctChecked<TSource>(this IEnumerable<TSource> source, IEqualityComparer<TSource> comparer) { var ret = source != null ? source.Distinct(comparer) : null; return ret; }

        public static IEnumerable<TResult> OfTypeChecked<TResult>(this IEnumerable source) { var ret = source != null ? source.OfType<TResult>() : null; return ret; }

        public static IOrderedEnumerable<TSource> OrderByChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) { var ret = source != null ? source.OrderBy(keySelector) : null; return ret; }
        public static IOrderedEnumerable<TSource> OrderByChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) { var ret = source != null ? source.OrderBy(keySelector, comparer) : null; return ret; }
        public static IOrderedEnumerable<TSource> OrderByDescendingChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) { var ret = source != null ? source.OrderByDescending(keySelector) : null; return ret; }
        public static IOrderedEnumerable<TSource> OrderByDescendingChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) { var ret = source != null ? source.OrderByDescending(keySelector, comparer) : null; return ret; }

        public static IEnumerable<TSource> ReverseChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.Reverse() : null; return ret; }
        public static IEnumerable<TResult> SelectChecked<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, int, TResult> selector) { var ret = source != null ? source.Select(selector) : null; return ret; }
        public static IEnumerable<TResult> SelectChecked<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) { var ret = source != null ? source.Select(selector) : null; return ret; }
        public static IEnumerable<TResult> SelectManyChecked<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector) { var ret = source != null ? source.SelectMany(selector) : null; return ret; }
        public static IEnumerable<TResult> SelectManyChecked<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, int, IEnumerable<TResult>> selector) { var ret = source != null ? source.SelectMany(selector) : null; return ret; }
        public static IEnumerable<TResult> SelectManyChecked<TSource, TCollection, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) { var ret = source != null ? source.SelectMany(collectionSelector, resultSelector) : null; return ret; }
        public static IEnumerable<TResult> SelectManyChecked<TSource, TCollection, TResult>(this IEnumerable<TSource> source, Func<TSource, int, IEnumerable<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) { var ret = source != null ? source.SelectMany(collectionSelector, resultSelector) : null; return ret; }
        public static bool SequenceEqualChecked<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) { var ret = first != null ? first.SequenceEqual(second) : false; return ret; }
        public static bool SequenceEqualChecked<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer) { var ret = first != null ? first.SequenceEqual(second, comparer) : false; return ret; }
        public static TSource SingleChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.Single() : default(TSource); return ret; }
        public static TSource SingleChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.Single(predicate) : default(TSource); return ret; }
        public static TSource SingleOrDefaultChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.SingleOrDefault() : default(TSource); return ret; }
        public static TSource SingleOrDefaultChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.SingleOrDefault(predicate) : default(TSource); return ret; }
        public static IEnumerable<TSource> SkipChecked<TSource>(this IEnumerable<TSource> source, int count) { var ret = source != null ? source.Skip(count) : null; return ret; }
        public static IEnumerable<TSource> SkipWhileChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.SkipWhileChecked(predicate) : null; return ret; }
        public static IEnumerable<TSource> SkipWhileChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate) { var ret = source != null ? source.SkipWhileChecked(predicate) : null; return ret; }
        public static decimal? SumChecked(this IEnumerable<decimal?> source) { var ret = source != null ? source.Sum() : null; return ret; }
        public static decimal SumChecked(this IEnumerable<decimal> source) { var ret = source != null ? source.Sum() : 0; return ret; }
        public static double? SumChecked(this IEnumerable<double?> source) { var ret = source != null ? source.Sum() : null; return ret; }
        public static double SumChecked(this IEnumerable<double> source) { var ret = source != null ? source.Sum() : 0; return ret; }
        public static float? SumChecked(this IEnumerable<float?> source) { var ret = source != null ? source.Sum() : null; return ret; }
        public static float SumChecked(this IEnumerable<float> source) { var ret = source != null ? source.Sum() : 0; return ret; }
        public static int? SumChecked(this IEnumerable<int?> source) { var ret = source != null ? source.Sum() : null; return ret; }
        public static int SumChecked(this IEnumerable<int> source) { var ret = source != null ? source.Sum() : 0; return ret; }
        public static long? SumChecked(this IEnumerable<long?> source) { var ret = source != null ? source.Sum() : null; return ret; }
        public static long SumChecked(this IEnumerable<long> source) { var ret = source != null ? source.Sum() : 0; return ret; }
        public static decimal? SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal?> selector) { var ret = source != null ? source.Sum(selector) : null; return ret; }
        public static decimal SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, decimal> selector) { var ret = source != null ? source.Sum(selector) : 0; return ret; }
        public static double? SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, double?> selector) { var ret = source != null ? source.Sum(selector) : null; return ret; }
        public static double SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, double> selector) { var ret = source != null ? source.Sum(selector) : 0; return ret; }
        public static float? SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, float?> selector) { var ret = source != null ? source.Sum(selector) : null; return ret; }
        public static float SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, float> selector) { var ret = source != null ? source.Sum(selector) : 0; return ret; }
        public static int? SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, int?> selector) { var ret = source != null ? source.Sum(selector) : null; return ret; }
        public static int SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, int> selector) { var ret = source != null ? source.Sum(selector) : 0; return ret; }
        public static long? SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, long?> selector) { var ret = source != null ? source.Sum(selector) : null; return ret; }
        public static long SumChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, long> selector) { var ret = source != null ? source.Sum(selector) : 0; return ret; }
        public static IEnumerable<TSource> TakeChecked<TSource>(this IEnumerable<TSource> source, int count) { var ret = source != null ? source.Take(count) : null; return ret; }
        public static IEnumerable<TSource> TakeWhileChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.TakeWhile(predicate) : null; return ret; }
        public static IEnumerable<TSource> TakeWhileChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate) { var ret = source != null ? source.TakeWhile(predicate) : null; return ret; }
        public static IOrderedEnumerable<TSource> ThenByChecked<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) { var ret = source != null ? source.ThenBy(keySelector) : null; return ret; }
        public static IOrderedEnumerable<TSource> ThenByChecked<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) { var ret = source != null ? source.ThenBy(keySelector, comparer) : null; return ret; }
        public static IOrderedEnumerable<TSource> ThenByDescendingChecked<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector) { var ret = source != null ? source.ThenByDescending(keySelector) : null; return ret; }
        public static IOrderedEnumerable<TSource> ThenByDescendingChecked<TSource, TKey>(this IOrderedEnumerable<TSource> source, Func<TSource, TKey> keySelector, IComparer<TKey> comparer) { var ret = source != null ? source.ThenByDescending(keySelector, comparer) : null; return ret; }
        public static TSource[] ToArrayChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.ToArray() : null; return ret; }
        public static Dictionary<TKey, TSource> ToDictionaryChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) { var ret = source != null ? source.ToDictionary(keySelector) : null; return ret; }
        public static Dictionary<TKey, TElement> ToDictionaryChecked<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector) { var ret = source != null ? source.ToDictionary(keySelector, elementSelector) : null; return ret; }
        public static Dictionary<TKey, TSource> ToDictionaryChecked<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, IEqualityComparer<TKey> comparer) { var ret = source != null ? source.ToDictionary(keySelector, comparer) : null; return ret; }
        public static Dictionary<TKey, TElement> ToDictionaryChecked<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey> comparer) { var ret = source != null ? source.ToDictionary(keySelector, elementSelector, comparer) : null; return ret; }

        public static List<TSource> ToListChecked<TSource>(this IEnumerable<TSource> source) { var ret = source != null ? source.ToList() : null; return ret; }
        public static IEnumerable<TSource> UnionChecked<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second) { var ret = first != null && second != null ? first.Union(second) : (first ?? second); return ret; }
        public static IEnumerable<TSource> UnionChecked<TSource>(this IEnumerable<TSource> first, IEnumerable<TSource> second, IEqualityComparer<TSource> comparer) { var ret = first != null && second != null ? first.Union(second) : (first ?? second); return ret; }
        public static IEnumerable<TSource> WhereChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate) { var ret = source != null ? source.Where(predicate) : null; return ret; }
        public static IEnumerable<TSource> WhereChecked<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate) { var ret = source != null ? source.Where(predicate) : null; return ret; }
    }
}
