﻿using Microsoft.Extensions.Logging;
using Namespace2Xml.Semantics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namespace2Xml.Syntax;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Namespace2Xml.Formatters
{
    public class YamlFormatter : JsonYamlFormatterBase
    {
        public YamlFormatter(
            Func<Stream> outputStreamFactory,
            [NullGuard.AllowNull] IReadOnlyList<string> outputPrefix,
            IQualifiedNameMatchDictionary<string> keys,
            IQualifiedNameMatchList arrays,
            IQualifiedNameMatchList strings,
            ILogger<YamlFormatter> logger)
            : base(outputStreamFactory,
                  outputPrefix,
                  keys,
                  arrays,
                  strings,
                  logger)
        { }

        protected override async Task DoWrite(ProfileTree tree, Stream stream, CancellationToken cancellationToken)
        {
            var serializer = new SerializerBuilder()
                .WithEventEmitter(nextEmitter => new QuoteStringEventEmitter(nextEmitter))
                .Build();

            using var writer = new StreamWriter(stream);

            await writer.WriteAsync(
                serializer
                    .Serialize(ToObject(tree, new string[0]))
                    .ToCharArray(),
                cancellationToken);
        }

        private object ToObject(ProfileTree tree, string[] prefix)
        {
            string[] newPrefix = prefix
                                .Concat(new[] { tree.NameString })
                                .ToArray();

            switch (tree)
            {
                case ProfileTreeNode node:
                    if (keys.TryMatch(newPrefix.ToQualifiedName(), out var key))
                    {
                        var nodes = node.Children
                            .OfType<ProfileTreeNode>()
                            .Select(child =>
                            {
                                var keyPayload = new Payload(
                                    new[] { key }.ToQualifiedName(),
                                    new IValueToken[] { new TextValueToken(child.NameString) },
                                    tree.GetFirstSourceMark());
                                var keyLeaf = new ProfileTreeLeaf(
                                    keyPayload,
                                    Enumerable.Empty<Comment>(),
                                    newPrefix.ToQualifiedName());

                                return ToObject(
                                    new ProfileTreeNode(node.Name, new [] { keyLeaf }.Concat(child.Children)),
                                    newPrefix.Concat(new[] { child.NameString }).ToArray());
                            }).ToList();
                        return nodes;
                    }

                    if (arrays.IsMatch(newPrefix.ToQualifiedName()))
                    {
                        return node.Children
                            .Select(x => new
                            {
                                child = x,
                                arrIndex = int.TryParse(x.NameString, out var index) ? index : int.MaxValue,
                            })
                            .OrderBy(x => x.arrIndex)
                            .Select(x => ToObject(x.child, newPrefix)).ToArray();
                    }

                    return node.Children.ToDictionary(child => child.NameString,
                        child => ToObject(child, newPrefix));

                case ProfileTreeLeaf leaf:
                    return ToObjectSingleValue(leaf.Value, newPrefix);

                default:
                    throw new NotSupportedException();
            }
        }

        private object ToObjectSingleValue(string value, string[] prefix)
        {
            if (strings.IsMatch(prefix.ToQualifiedName()))
                return value;

            if (value == "[]")
                return new string[0];

            if (value == "{}")
                return new Dictionary<string, string>();

            (var typedValue, var success) = TryParse(value);

            return success ? typedValue : value;
        }

        private class QuoteStringEventEmitter : ChainedEventEmitter
        {
            public QuoteStringEventEmitter(IEventEmitter nextEmitter) : base(nextEmitter)
            { }

            public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
            {
                if (eventInfo.Source.Value is string value && TryParse(value).success)
                    eventInfo.Style = ScalarStyle.SingleQuoted;
                base.Emit(eventInfo, emitter);
            }
        }
    }
}
