// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Based on: https://learn.microsoft.com/windows/apps/develop/performance/optimize-gridview-and-listview
// Implements incremental loading to prevent UI freezing with large datasets

using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace QuickView.Collections;

/// <summary>
/// Incremental loading collection that loads items in batches.
/// Prevents UI freezing by virtualizing data loading.
/// </summary>
public class IncrementalLoadingCollection<T> : ObservableCollection<T>, ISupportIncrementalLoading
{
    private readonly Func<uint, CancellationToken, Task<IEnumerable<T>>> _loadMoreItemsFunc;
    private readonly uint _batchSize;
    private bool _hasMoreItems;
    private bool _isLoading;

    public IncrementalLoadingCollection(
        Func<uint, CancellationToken, Task<IEnumerable<T>>> loadMoreItemsFunc,
        uint batchSize = 50)
    {
        _loadMoreItemsFunc = loadMoreItemsFunc;
        _batchSize = batchSize;
        _hasMoreItems = true;
        _isLoading = false;
    }

    public bool HasMoreItems => _hasMoreItems && !_isLoading;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        if (_isLoading)
            return AsyncInfo.Run(c => Task.FromResult(new LoadMoreItemsResult { Count = 0 }));

        return AsyncInfo.Run(async cancelToken =>
        {
            _isLoading = true;

            try
            {
                var items = await _loadMoreItemsFunc(_batchSize, cancelToken);
                var itemsList = items?.ToList() ?? new List<T>();

                if (itemsList.Count == 0)
                {
                    _hasMoreItems = false;
                    return new LoadMoreItemsResult { Count = 0 };
                }

                // Add items in smaller chunks to avoid UI freeze
                const int CHUNK_SIZE = 10;
                uint totalAdded = 0;

                for (int i = 0; i < itemsList.Count; i += CHUNK_SIZE)
                {
                    var chunk = itemsList.Skip(i).Take(CHUNK_SIZE);
                    foreach (var item in chunk)
                    {
                        Add(item);
                        totalAdded++;
                    }

                    // Small delay to let UI breathe
                    if (i + CHUNK_SIZE < itemsList.Count)
                        await Task.Delay(1, cancelToken);
                }

                return new LoadMoreItemsResult { Count = totalAdded };
            }
            catch
            {
                _hasMoreItems = false;
                return new LoadMoreItemsResult { Count = 0 };
            }
            finally
            {
                _isLoading = false;
            }
        });
    }

    public void Reset()
    {
        Clear();
        _hasMoreItems = true;
        _isLoading = false;
    }
}
