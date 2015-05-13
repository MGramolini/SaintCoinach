﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaintCoinach.Text.Nodes {
    public class EmptyElement : IStringNode {
        private readonly TagType _Tag;

        public TagType Tag { get { return _Tag; } }
        NodeType IStringNode.Type { get { return NodeType.EmptyElement; } }
        NodeFlags IStringNode.Flags { get { return NodeFlags.OpenTag | NodeFlags.CloseTag | NodeFlags.IsExpression; } }

        public EmptyElement(TagType tag) {
            _Tag = tag;
        }

        public override string ToString() {
            var sb = new StringBuilder();
            ToString(sb);
            return sb.ToString();
        }
        public void ToString(StringBuilder builder) {
            builder.Append(StringTokens.TagOpen);
            builder.Append(Tag);
            builder.Append(StringTokens.ElementClose);
            builder.Append(StringTokens.TagClose);
        }
    }
}