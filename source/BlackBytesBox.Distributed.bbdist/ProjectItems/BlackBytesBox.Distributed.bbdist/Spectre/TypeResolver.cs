﻿using System;

using Spectre.Console.Cli;

namespace BlackBytesBox.Distributed.bbdist.Spectre
{
    public sealed class TypeResolver : ITypeResolver, IDisposable
    {
        private readonly IServiceProvider _provider;

        public TypeResolver(IServiceProvider provider) => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        public object? Resolve(Type? type) => type == null ? null : _provider.GetService(type);

        public void Dispose()
        { if (_provider is IDisposable disposable) disposable.Dispose(); }
    }
}