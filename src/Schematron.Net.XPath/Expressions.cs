/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Schematron.Net.XPath
{
    /// <summary>
    /// What an expression is statically known to return (used to decide whether a predicate is positional).
    /// </summary>
    internal enum ResultKind
    {
        Unknown,
        Boolean,
        Nodes,
        String,
        Numeric
    }


    /// <summary>
    /// A node of the expression tree.
    /// </summary>
    internal abstract class Expr
    {
        private static readonly Expr[] _NoChildren = new Expr[0];


        public abstract XdmValue Evaluate(EvalContext context);


        public virtual bool EffectiveBooleanValue(EvalContext context)
        {
            return Operations.EffectiveBooleanValue(Evaluate(context));
        }


        public virtual ResultKind ResultKind => ResultKind.Unknown;


        public virtual IEnumerable<Expr> Children => _NoChildren;


        /// <summary>
        /// True if the expression (or any subexpression) calls position() or last().
        /// </summary>
        public virtual bool UsesPosition
        {
            get
            {
                foreach (Expr child in Children)
                {
                    if (child.UsesPosition)
                    {
                        return true;
                    }
                }
                return false;
            }
        }


        /// <summary>
        /// True if the value of the expression in a predicate can never be numeric and does not depend on the
        /// context position, i.e. the predicate is a pure filter.
        /// </summary>
        public bool IsNonPositionalPredicate => (ResultKind == ResultKind.Boolean || ResultKind == ResultKind.Nodes || ResultKind == ResultKind.String) && !UsesPosition;
    }


    internal sealed class LiteralExpr : Expr
    {
        public readonly XdmAtomicValue Value;
        private readonly XdmValue _Sequence;


        public LiteralExpr(XdmAtomicValue value)
        {
            Value = value;
            _Sequence = XdmValue.Of(value);
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            return _Sequence;
        }


        public override ResultKind ResultKind => Value is NumericValue ? ResultKind.Numeric : Value is BooleanValue ? ResultKind.Boolean : Value is StringAtomic ? ResultKind.String : ResultKind.Unknown;
    }


    internal sealed class EmptySequenceExpr : Expr
    {
        public static readonly EmptySequenceExpr Instance = new EmptySequenceExpr();


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Empty;
        }


        public override ResultKind ResultKind => ResultKind.Nodes;
    }


    internal sealed class SequenceExpr : Expr
    {
        private readonly Expr[] _Items;


        public SequenceExpr(Expr[] items)
        {
            _Items = items;
        }


        public override IEnumerable<Expr> Children => _Items;


        public override XdmValue Evaluate(EvalContext context)
        {
            List<XdmItem> result = new List<XdmItem>();
            foreach (Expr item in _Items)
            {
                result.AddRange(item.Evaluate(context));
            }
            return XdmValue.FromList(result);
        }
    }


    internal sealed class ContextItemExpr : Expr
    {
        public static readonly ContextItemExpr Instance = new ContextItemExpr();


        public override XdmValue Evaluate(EvalContext context)
        {
            if (context.ContextItem == null)
            {
                throw new XPathException("XPDY0002", "The context item is absent");
            }
            return XdmValue.Of(context.ContextItem);
        }
    }


    /// <summary>
    /// The leading "/" of an absolute path: the root of the tree containing the context node, which must be a document node.
    /// </summary>
    internal sealed class RootExpr : Expr
    {
        public static readonly RootExpr Instance = new RootExpr();


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmNode root = context.ContextNode.Root;
            if (root.Kind != XdmNodeKind.Document)
            {
                throw new XPathException("XPDY0050", "The root of the tree containing the context item is not a document node");
            }
            return XdmValue.Of(root);
        }


        public override ResultKind ResultKind => ResultKind.Nodes;
    }


    internal sealed class LocalVariableExpr : Expr
    {
        public readonly int Slot;
        public readonly string Name;


        public LocalVariableExpr(int slot, string name)
        {
            Slot = slot;
            Name = name;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            return context.Locals[Slot];
        }
    }


    /// <summary>
    /// A reference to a variable that is not bound inside the expression (a Schematron let or an XSLT variable).
    /// </summary>
    internal sealed class ExternalVariableExpr : Expr
    {
        public readonly string Name;


        public ExternalVariableExpr(string name)
        {
            Name = name;
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue value;
            if (context.Variables != null && context.Variables.TryResolve(Name, out value))
            {
                return value;
            }
            throw new XPathException("XPST0008", "Variable $" + Name + " has not been declared");
        }
    }


    internal sealed class ForExpr : Expr
    {
        private readonly int _Slot;
        private readonly Expr _In;
        private readonly Expr _Return;


        public ForExpr(int slot, Expr inExpr, Expr returnExpr)
        {
            _Slot = slot;
            _In = inExpr;
            _Return = returnExpr;
        }


        public override IEnumerable<Expr> Children => new[] { _In, _Return };


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue input = _In.Evaluate(context);
            if (input.Count == 0)
            {
                return XdmValue.Empty;
            }
            XdmValue saved = context.Locals[_Slot];
            List<XdmItem> result = new List<XdmItem>();
            try
            {
                foreach (XdmItem item in input)
                {
                    context.Locals[_Slot] = XdmValue.Of(item);
                    result.AddRange(_Return.Evaluate(context));
                }
            }
            finally
            {
                context.Locals[_Slot] = saved;
            }
            return XdmValue.FromList(result);
        }
    }


    internal sealed class LetExpr : Expr
    {
        private readonly int _Slot;
        private readonly Expr _Value;
        private readonly Expr _Return;


        public LetExpr(int slot, Expr value, Expr returnExpr)
        {
            _Slot = slot;
            _Value = value;
            _Return = returnExpr;
        }


        public override IEnumerable<Expr> Children => new[] { _Value, _Return };


        public override ResultKind ResultKind => _Return.ResultKind;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue saved = context.Locals[_Slot];
            try
            {
                context.Locals[_Slot] = _Value.Evaluate(context);
                return _Return.Evaluate(context);
            }
            finally
            {
                context.Locals[_Slot] = saved;
            }
        }
    }


    internal sealed class QuantifiedExpr : Expr
    {
        private readonly bool _Every;
        private readonly int _Slot;
        private readonly Expr _In;
        private readonly Expr _Satisfies;


        public QuantifiedExpr(bool every, int slot, Expr inExpr, Expr satisfies)
        {
            _Every = every;
            _Slot = slot;
            _In = inExpr;
            _Satisfies = satisfies;
        }


        public override IEnumerable<Expr> Children => new[] { _In, _Satisfies };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Of(EffectiveBooleanValue(context));
        }


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            XdmValue input = _In.Evaluate(context);
            XdmValue saved = context.Locals[_Slot];
            try
            {
                foreach (XdmItem item in input)
                {
                    context.Locals[_Slot] = XdmValue.Of(item);
                    bool satisfied = _Satisfies.EffectiveBooleanValue(context);
                    if (satisfied != _Every)
                    {
                        return satisfied;
                    }
                }
                return _Every;
            }
            finally
            {
                context.Locals[_Slot] = saved;
            }
        }
    }


    internal sealed class IfExpr : Expr
    {
        private readonly Expr _Condition;
        private readonly Expr _Then;
        private readonly Expr _Else;


        public IfExpr(Expr condition, Expr then, Expr otherwise)
        {
            _Condition = condition;
            _Then = then;
            _Else = otherwise;
        }


        public override IEnumerable<Expr> Children => new[] { _Condition, _Then, _Else };


        public override ResultKind ResultKind => _Then.ResultKind == _Else.ResultKind ? _Then.ResultKind : ResultKind.Unknown;


        public override XdmValue Evaluate(EvalContext context)
        {
            return _Condition.EffectiveBooleanValue(context) ? _Then.Evaluate(context) : _Else.Evaluate(context);
        }


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            return _Condition.EffectiveBooleanValue(context) ? _Then.EffectiveBooleanValue(context) : _Else.EffectiveBooleanValue(context);
        }
    }


    internal sealed class LogicalExpr : Expr
    {
        private readonly bool _IsAnd;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public LogicalExpr(bool isAnd, Expr left, Expr right)
        {
            _IsAnd = isAnd;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Of(EffectiveBooleanValue(context));
        }


        /// <summary>
        /// As in Saxon 12: a dynamic error of the first operand is deferred; if the second operand decides the
        /// result (false for "and", true for "or") the error is ignored, else it is raised.
        /// </summary>
        public override bool EffectiveBooleanValue(EvalContext context)
        {
            XPathException deferred = null;
            try
            {
                bool left = _Left.EffectiveBooleanValue(context);
                if (left != _IsAnd)
                {
                    return left;
                }
            }
            catch (XPathException e)
            {
                deferred = e;
            }
            bool right = _Right.EffectiveBooleanValue(context);
            if (right != _IsAnd)
            {
                return right;
            }
            if (deferred != null)
            {
                throw deferred;
            }
            return _IsAnd;
        }
    }


    internal sealed class GeneralComparisonExpr : Expr
    {
        private readonly ComparisonOperator _Operator;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public GeneralComparisonExpr(ComparisonOperator op, Expr left, Expr right)
        {
            _Operator = op;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Of(EffectiveBooleanValue(context));
        }


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            return Operations.GeneralCompare(_Left.Evaluate(context), _Operator, _Right.Evaluate(context));
        }
    }


    internal sealed class ValueComparisonExpr : Expr
    {
        private readonly ComparisonOperator _Operator;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public ValueComparisonExpr(ComparisonOperator op, Expr left, Expr right)
        {
            _Operator = op;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmAtomicValue a = Operations.AtomizeOptional(_Left.Evaluate(context), "the first operand of a value comparison");
            if (a == null)
            {
                return XdmValue.Empty;
            }
            XdmAtomicValue b = Operations.AtomizeOptional(_Right.Evaluate(context), "the second operand of a value comparison");
            if (b == null)
            {
                return XdmValue.Empty;
            }
            if (a.Type == XsType.UntypedAtomic)
            {
                a = new StringAtomic(((StringAtomic)a).Value);
            }
            if (b.Type == XsType.UntypedAtomic)
            {
                b = new StringAtomic(((StringAtomic)b).Value);
            }
            return XdmValue.Of(Operations.ValueCompare(a, _Operator, b));
        }
    }


    internal sealed class NodeComparisonExpr : Expr
    {
        /// <summary>"is", "&lt;&lt;" or "&gt;&gt;".</summary>
        private readonly string _Operator;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public NodeComparisonExpr(string op, Expr left, Expr right)
        {
            _Operator = op;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmNode a = _Single(_Left.Evaluate(context));
            XdmNode b = _Single(_Right.Evaluate(context));
            if (a == null || b == null)
            {
                return XdmValue.Empty;
            }
            int c = XdmNode.CompareOrder(a, b);
            switch (_Operator)
            {
                case "is":
                    return XdmValue.Of(a == b);
                case "<<":
                    return XdmValue.Of(c < 0);
                default:
                    return XdmValue.Of(c > 0);
            }
        }


        private static XdmNode _Single(XdmValue s)
        {
            if (s.Count == 0)
            {
                return null;
            }
            if (s.Count > 1 || !(s[0] is XdmNode))
            {
                throw new XPathException("XPTY0004", "The operands of a node comparison must be single nodes");
            }
            return (XdmNode)s[0];
        }
    }


    internal sealed class RangeExpr : Expr
    {
        private readonly Expr _From;
        private readonly Expr _To;


        public RangeExpr(Expr from, Expr to)
        {
            _From = from;
            _To = to;
        }


        public override IEnumerable<Expr> Children => new[] { _From, _To };


        public override XdmValue Evaluate(EvalContext context)
        {
            BigInteger? from = _ToInteger(_From.Evaluate(context));
            BigInteger? to = _ToInteger(_To.Evaluate(context));
            if (!from.HasValue || !to.HasValue || from.Value > to.Value)
            {
                return XdmValue.Empty;
            }
            if (to.Value - from.Value > 10000000)
            {
                throw new XPathException("XPDY0130", "Range expression is too large");
            }
            List<XdmItem> items = new List<XdmItem>();
            for (BigInteger i = from.Value; i <= to.Value; i++)
            {
                items.Add(IntegerValue.Get(i));
            }
            return XdmValue.FromList(items);
        }


        private static BigInteger? _ToInteger(XdmValue s)
        {
            XdmAtomicValue a = Operations.AtomizeOptional(s, "an operand of a range expression");
            if (a == null)
            {
                return null;
            }
            if (a.Type == XsType.UntypedAtomic)
            {
                a = Casting.Cast(a, XsType.Integer);
            }
            if (a is IntegerValue i)
            {
                return i.Value;
            }
            throw new XPathException("XPTY0004", "The operands of a range expression must be integers");
        }
    }


    internal sealed class ArithmeticExpr : Expr
    {
        private readonly ArithmeticOperator _Operator;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public ArithmeticExpr(ArithmeticOperator op, Expr left, Expr right)
        {
            _Operator = op;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Numeric;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmAtomicValue a = Operations.AtomizeOptional(_Left.Evaluate(context), "the first operand of '" + _OperatorName + "'");
            if (a == null)
            {
                return XdmValue.Empty;
            }
            XdmAtomicValue b = Operations.AtomizeOptional(_Right.Evaluate(context), "the second operand of '" + _OperatorName + "'");
            if (b == null)
            {
                return XdmValue.Empty;
            }
            return XdmValue.Of(Operations.Arithmetic(a, _Operator, b));
        }


        private string _OperatorName
        {
            get
            {
                switch (_Operator)
                {
                    case ArithmeticOperator.Add:
                        return "+";
                    case ArithmeticOperator.Subtract:
                        return "-";
                    case ArithmeticOperator.Multiply:
                        return "*";
                    case ArithmeticOperator.Divide:
                        return "div";
                    case ArithmeticOperator.IntegerDivide:
                        return "idiv";
                    default:
                        return "mod";
                }
            }
        }
    }


    internal sealed class UnaryMinusExpr : Expr
    {
        private readonly Expr _Operand;


        public UnaryMinusExpr(Expr operand)
        {
            _Operand = operand;
        }


        public override IEnumerable<Expr> Children => new[] { _Operand };


        public override ResultKind ResultKind => ResultKind.Numeric;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmAtomicValue a = Operations.AtomizeOptional(_Operand.Evaluate(context), "the operand of unary minus");
            if (a == null)
            {
                return XdmValue.Empty;
            }
            if (a.Type == XsType.UntypedAtomic)
            {
                a = Casting.Cast(a, XsType.Double);
            }
            NumericValue n = a as NumericValue;
            if (n == null)
            {
                throw new XPathException("XPTY0004", "Unary minus is not defined for " + a.Type.QualifiedName);
            }
            return XdmValue.Of(Operations.Negate(n));
        }
    }


    internal sealed class UnaryPlusExpr : Expr
    {
        private readonly Expr _Operand;


        public UnaryPlusExpr(Expr operand)
        {
            _Operand = operand;
        }


        public override IEnumerable<Expr> Children => new[] { _Operand };


        public override ResultKind ResultKind => ResultKind.Numeric;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmAtomicValue a = Operations.AtomizeOptional(_Operand.Evaluate(context), "the operand of unary plus");
            if (a == null)
            {
                return XdmValue.Empty;
            }
            if (a.Type == XsType.UntypedAtomic)
            {
                a = Casting.Cast(a, XsType.Double);
            }
            if (!(a is NumericValue))
            {
                throw new XPathException("XPTY0004", "Unary plus is not defined for " + a.Type.QualifiedName);
            }
            return XdmValue.Of(a);
        }
    }


    internal enum SetOperator
    {
        Union,
        Intersect,
        Except
    }


    internal sealed class SetExpr : Expr
    {
        private readonly SetOperator _Operator;
        private readonly Expr _Left;
        private readonly Expr _Right;


        public SetOperator Operator => _Operator;
        public Expr Left => _Left;
        public Expr Right => _Right;


        public SetExpr(SetOperator op, Expr left, Expr right)
        {
            _Operator = op;
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override ResultKind ResultKind => ResultKind.Nodes;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue left = _Left.Evaluate(context);
            XdmValue right = _Right.Evaluate(context);
            List<XdmNode> l = NodeSorting.ToNodeList(left, "union/intersect/except");
            List<XdmNode> r = NodeSorting.ToNodeList(right, "union/intersect/except");
            List<XdmNode> result;
            switch (_Operator)
            {
                case SetOperator.Union:
                    result = new List<XdmNode>(l.Count + r.Count);
                    result.AddRange(l);
                    result.AddRange(r);
                    break;
                case SetOperator.Intersect:
                    {
                        HashSet<XdmNode> set = new HashSet<XdmNode>(r);
                        result = l.Where(set.Contains).ToList();
                        break;
                    }
                default:
                    {
                        HashSet<XdmNode> set = new HashSet<XdmNode>(r);
                        result = l.Where(n => !set.Contains(n)).ToList();
                        break;
                    }
            }
            NodeSorting.SortAndDeduplicate(result);
            return XdmValue.FromNodes(result);
        }
    }


    internal sealed class InstanceOfExpr : Expr
    {
        private readonly Expr _Operand;
        private readonly XPathSequenceType _Type;


        public InstanceOfExpr(Expr operand, XPathSequenceType type)
        {
            _Operand = operand;
            _Type = type;
        }


        public override IEnumerable<Expr> Children => new[] { _Operand };


        public override ResultKind ResultKind => ResultKind.Boolean;


        public override XdmValue Evaluate(EvalContext context)
        {
            return XdmValue.Of(_Type.Matches(_Operand.Evaluate(context)));
        }
    }


    internal sealed class TreatExpr : Expr
    {
        private readonly Expr _Operand;
        private readonly XPathSequenceType _Type;


        public TreatExpr(Expr operand, XPathSequenceType type)
        {
            _Operand = operand;
            _Type = type;
        }


        public override IEnumerable<Expr> Children => new[] { _Operand };


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue value = _Operand.Evaluate(context);
            if (!_Type.Matches(value))
            {
                throw new XPathException("XPDY0050", "The value does not match the required type " + _Type + " of 'treat as'");
            }
            return value;
        }
    }


    internal sealed class CastExpr : Expr
    {
        private readonly Expr _Operand;
        private readonly XsType _Type;
        private readonly bool _AllowEmpty;
        private readonly bool _Castable;


        public CastExpr(Expr operand, XsType type, bool allowEmpty, bool castable)
        {
            _Operand = operand;
            _Type = type;
            _AllowEmpty = allowEmpty;
            _Castable = castable;
        }


        public override IEnumerable<Expr> Children => new[] { _Operand };


        public override ResultKind ResultKind => _Castable ? ResultKind.Boolean : (_Type.IsNumeric ? ResultKind.Numeric : _Type == XsType.Boolean ? ResultKind.Boolean : ResultKind.Unknown);


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue atomized = Operations.Atomize(_Operand.Evaluate(context));
            if (_Castable)
            {
                if (atomized.Count == 0)
                {
                    return XdmValue.Of(_AllowEmpty);
                }
                if (atomized.Count > 1)
                {
                    return XdmValue.False;
                }
                return XdmValue.Of(Casting.IsCastable((XdmAtomicValue)atomized[0], _Type));
            }
            if (atomized.Count == 0)
            {
                if (_AllowEmpty)
                {
                    return XdmValue.Empty;
                }
                throw new XPathException("XPTY0004", "An empty sequence is not allowed as the operand of 'cast as " + _Type.QualifiedName + "'");
            }
            if (atomized.Count > 1)
            {
                throw new XPathException("XPTY0004", "A sequence of more than one item is not allowed as the value in 'cast as' expression " + Operations._DescribeSequence(atomized));
            }
            return XdmValue.Of(Casting.Cast((XdmAtomicValue)atomized[0], _Type));
        }
    }


    /// <summary>
    /// The XPath 3.0 string concatenation operator "||".
    /// </summary>
    internal sealed class StringConcatExpr : Expr
    {
        private readonly Expr[] _Operands;


        public StringConcatExpr(Expr[] operands)
        {
            _Operands = operands;
        }


        public override IEnumerable<Expr> Children => _Operands;


        public override ResultKind ResultKind => ResultKind.String;


        public override XdmValue Evaluate(EvalContext context)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Expr operand in _Operands)
            {
                XdmAtomicValue a = Operations.AtomizeOptional(operand.Evaluate(context), "an operand of '||'");
                if (a != null)
                {
                    sb.Append(a.StringValue);
                }
            }
            return XdmValue.Of(sb.ToString());
        }
    }


    /// <summary>
    /// The XPath 3.0 simple map operator "!".
    /// </summary>
    internal sealed class SimpleMapExpr : Expr
    {
        private readonly Expr _Left;
        private readonly Expr _Right;


        public SimpleMapExpr(Expr left, Expr right)
        {
            _Left = left;
            _Right = right;
        }


        public override IEnumerable<Expr> Children => new[] { _Left, _Right };


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue input = _Left.Evaluate(context);
            XdmItem savedItem = context.ContextItem;
            int savedPosition = context.Position;
            int savedSize = context.Size;
            List<XdmItem> result = new List<XdmItem>();
            try
            {
                for (int i = 0; i < input.Count; i++)
                {
                    context.ContextItem = input[i];
                    context.Position = i + 1;
                    context.Size = input.Count;
                    result.AddRange(_Right.Evaluate(context));
                }
            }
            finally
            {
                context.ContextItem = savedItem;
                context.Position = savedPosition;
                context.Size = savedSize;
            }
            return XdmValue.FromList(result);
        }
    }


    /// <summary>
    /// A path expression E1/E2/.../En: every step is evaluated once for every node returned by the previous one.
    /// </summary>
    internal sealed class PathExpr : Expr
    {
        public readonly Expr Start;
        public readonly Expr[] Steps;


        public PathExpr(Expr start, Expr[] steps)
        {
            Start = start;
            Steps = steps;
        }


        public override IEnumerable<Expr> Children
        {
            get
            {
                // only the start is evaluated with the outer focus
                yield return Start;
            }
        }


        public override ResultKind ResultKind => Steps[Steps.Length - 1] is AxisStepExpr ? ResultKind.Nodes : ResultKind.Unknown;


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            return Operations.EffectiveBooleanValue(Evaluate(context));
        }


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue current = Start.Evaluate(context);
            XdmItem savedItem = context.ContextItem;
            int savedPosition = context.Position;
            int savedSize = context.Size;
            try
            {
                foreach (Expr step in Steps)
                {
                    if (current.Count == 0)
                    {
                        return XdmValue.Empty;
                    }
                    AxisStepExpr axisStep = step as AxisStepExpr;
                    if (current.Count == 1)
                    {
                        XdmNode node = current[0] as XdmNode;
                        if (node == null)
                        {
                            throw new XPathException("XPTY0019", "The required item type of the first operand of '/' is node(), supplied value is " + current[0]);
                        }
                        if (axisStep != null)
                        {
                            current = axisStep.EvaluateFrom(node, context);
                            continue;
                        }
                        context.ContextItem = node;
                        context.Position = 1;
                        context.Size = 1;
                        current = _CheckStepResult(step.Evaluate(context));
                        continue;
                    }

                    List<XdmItem> result = new List<XdmItem>();
                    bool hasNodes = false;
                    bool hasAtomics = false;
                    bool sorted = true;
                    XdmNode last = null;
                    int count = current.Count;
                    for (int i = 0; i < count; i++)
                    {
                        XdmNode node = current[i] as XdmNode;
                        if (node == null)
                        {
                            throw new XPathException("XPTY0019", "The required item type of the first operand of '/' is node(), supplied value is " + current[i]);
                        }
                        XdmValue r;
                        if (axisStep != null)
                        {
                            r = axisStep.EvaluateFrom(node, context);
                        }
                        else
                        {
                            context.ContextItem = node;
                            context.Position = i + 1;
                            context.Size = count;
                            r = step.Evaluate(context);
                        }
                        foreach (XdmItem item in r)
                        {
                            if (item is XdmNode n)
                            {
                                hasNodes = true;
                                if (last != null && sorted && XdmNode.CompareOrder(last, n) >= 0)
                                {
                                    sorted = false;
                                }
                                last = n;
                            }
                            else
                            {
                                hasAtomics = true;
                            }
                            result.Add(item);
                        }
                    }
                    if (hasNodes && hasAtomics)
                    {
                        throw new XPathException("XPTY0018", "The result of the last step in a path expression contains both nodes and atomic values");
                    }
                    if (hasNodes && !sorted)
                    {
                        List<XdmNode> nodes = new List<XdmNode>(result.Count);
                        foreach (XdmItem item in result)
                        {
                            nodes.Add((XdmNode)item);
                        }
                        NodeSorting.SortAndDeduplicate(nodes);
                        current = XdmValue.FromNodes(nodes);
                    }
                    else
                    {
                        current = XdmValue.FromList(result);
                    }
                }
                return current;
            }
            finally
            {
                context.ContextItem = savedItem;
                context.Position = savedPosition;
                context.Size = savedSize;
            }
        }


        private static XdmValue _CheckStepResult(XdmValue r)
        {
            if (r.Count <= 1)
            {
                return r;
            }
            bool hasNodes = false;
            bool hasAtomics = false;
            bool sorted = true;
            XdmNode last = null;
            foreach (XdmItem item in r)
            {
                if (item is XdmNode n)
                {
                    hasNodes = true;
                    if (last != null && XdmNode.CompareOrder(last, n) >= 0)
                    {
                        sorted = false;
                    }
                    last = n;
                }
                else
                {
                    hasAtomics = true;
                }
            }
            if (hasNodes && hasAtomics)
            {
                throw new XPathException("XPTY0018", "The result of the last step in a path expression contains both nodes and atomic values");
            }
            if (hasNodes && !sorted)
            {
                List<XdmNode> nodes = r.Cast<XdmNode>().ToList();
                NodeSorting.SortAndDeduplicate(nodes);
                return XdmValue.FromNodes(nodes);
            }
            return r;
        }
    }


    internal enum Axis
    {
        Child,
        Descendant,
        Attribute,
        Self,
        DescendantOrSelf,
        FollowingSibling,
        Following,
        Namespace,
        Parent,
        Ancestor,
        PrecedingSibling,
        Preceding,
        AncestorOrSelf
    }


    /// <summary>
    /// An axis step with node test and predicates, evaluated relative to the context node.
    /// </summary>
    internal sealed class AxisStepExpr : Expr
    {
        public readonly Axis Axis;
        public readonly NodeTest Test;
        public readonly Expr[] Predicates;


        public AxisStepExpr(Axis axis, NodeTest test, Expr[] predicates)
        {
            Axis = axis;
            Test = test;
            Predicates = predicates;
        }


        /// <summary>
        /// Predicates are evaluated with their own focus, so position() inside them does not concern the outer expression.
        /// </summary>
        public override bool UsesPosition => false;


        public override ResultKind ResultKind => ResultKind.Nodes;


        public bool IsReverse => Axis == Axis.Parent || Axis == Axis.Ancestor || Axis == Axis.AncestorOrSelf || Axis == Axis.PrecedingSibling || Axis == Axis.Preceding;


        public override XdmValue Evaluate(EvalContext context)
        {
            return EvaluateFrom(context.ContextNode, context);
        }


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            if (Predicates.Length == 0)
            {
                return AxisIterator.Exists(context.ContextNode, Axis, Test);
            }
            return Evaluate(context).Count > 0;
        }


        public XdmValue EvaluateFrom(XdmNode origin, EvalContext context)
        {
            List<XdmNode> nodes = new List<XdmNode>();
            AxisIterator.Collect(origin, Axis, Test, nodes);
            foreach (Expr predicate in Predicates)
            {
                if (nodes.Count == 0)
                {
                    break;
                }
                nodes = Predicate.Apply(nodes, predicate, context);
            }
            if (IsReverse && nodes.Count > 1)
            {
                nodes.Reverse();
            }
            return XdmValue.FromNodes(nodes);
        }
    }


    /// <summary>
    /// A primary expression followed by predicates.
    /// </summary>
    internal sealed class FilterExpr : Expr
    {
        private readonly Expr _Primary;
        private readonly Expr[] _Predicates;


        public Expr Primary => _Primary;
        public Expr[] Predicates => _Predicates;


        public FilterExpr(Expr primary, Expr[] predicates)
        {
            _Primary = primary;
            _Predicates = predicates;
        }


        public override IEnumerable<Expr> Children => new[] { _Primary };


        public override ResultKind ResultKind => _Primary.ResultKind;


        public override XdmValue Evaluate(EvalContext context)
        {
            XdmValue value = _Primary.Evaluate(context);
            foreach (Expr predicate in _Predicates)
            {
                if (value.Count == 0)
                {
                    break;
                }
                value = Predicate.Apply(value, predicate, context);
            }
            return value;
        }
    }


    internal static class Predicate
    {
        /// <summary>
        /// The value of a predicate for the item at the given position: numeric values select by position,
        /// everything else by effective boolean value.
        /// </summary>
        private static bool _IsTrue(Expr predicate, EvalContext context, int position)
        {
            if (predicate.ResultKind == ResultKind.Boolean)
            {
                return predicate.EffectiveBooleanValue(context);
            }
            XdmValue r = predicate.Evaluate(context);
            if (r.Count == 1 && r[0] is NumericValue n)
            {
                return !n.IsNaN && n.ToDouble() == position && Operations.CompareNumeric(n, IntegerValue.Get(position)) == 0;
            }
            return Operations.EffectiveBooleanValue(r);
        }


        private static bool _TryGetConstantPosition(Expr predicate, out long position)
        {
            position = 0;
            if (predicate is LiteralExpr literal && literal.Value is IntegerValue i)
            {
                position = (long)i.Value;
                return true;
            }
            return false;
        }


        public static List<XdmNode> Apply(List<XdmNode> nodes, Expr predicate, EvalContext context)
        {
            long constant;
            if (_TryGetConstantPosition(predicate, out constant))
            {
                List<XdmNode> single = new List<XdmNode>(1);
                if (constant >= 1 && constant <= nodes.Count)
                {
                    single.Add(nodes[(int)constant - 1]);
                }
                return single;
            }
            if (predicate is FunctionCallExpr call && call.IsLast)
            {
                return new List<XdmNode> { nodes[nodes.Count - 1] };
            }

            XdmItem savedItem = context.ContextItem;
            int savedPosition = context.Position;
            int savedSize = context.Size;
            List<XdmNode> result = new List<XdmNode>();
            int size = nodes.Count;
            try
            {
                for (int i = 0; i < size; i++)
                {
                    context.ContextItem = nodes[i];
                    context.Position = i + 1;
                    context.Size = size;
                    if (_IsTrue(predicate, context, i + 1))
                    {
                        result.Add(nodes[i]);
                    }
                }
            }
            finally
            {
                context.ContextItem = savedItem;
                context.Position = savedPosition;
                context.Size = savedSize;
            }
            return result;
        }


        public static XdmValue Apply(XdmValue items, Expr predicate, EvalContext context)
        {
            long constant;
            if (_TryGetConstantPosition(predicate, out constant))
            {
                return constant >= 1 && constant <= items.Count ? XdmValue.Of(items[(int)constant - 1]) : XdmValue.Empty;
            }
            XdmItem savedItem = context.ContextItem;
            int savedPosition = context.Position;
            int savedSize = context.Size;
            List<XdmItem> result = new List<XdmItem>();
            int size = items.Count;
            try
            {
                for (int i = 0; i < size; i++)
                {
                    context.ContextItem = items[i];
                    context.Position = i + 1;
                    context.Size = size;
                    if (_IsTrue(predicate, context, i + 1))
                    {
                        result.Add(items[i]);
                    }
                }
            }
            finally
            {
                context.ContextItem = savedItem;
                context.Position = savedPosition;
                context.Size = savedSize;
            }
            return XdmValue.FromList(result);
        }
    }


    internal static class NodeSorting
    {
        private static readonly Comparison<XdmNode> _Comparison = XdmNode.CompareOrder;


        public static void SortAndDeduplicate(List<XdmNode> nodes)
        {
            if (nodes.Count < 2)
            {
                return;
            }
            bool sorted = true;
            for (int i = 1; i < nodes.Count; i++)
            {
                if (XdmNode.CompareOrder(nodes[i - 1], nodes[i]) >= 0)
                {
                    sorted = false;
                    break;
                }
            }
            if (sorted)
            {
                return;
            }
            nodes.Sort(_Comparison);
            int write = 1;
            for (int read = 1; read < nodes.Count; read++)
            {
                if (nodes[read] != nodes[write - 1])
                {
                    nodes[write++] = nodes[read];
                }
            }
            nodes.RemoveRange(write, nodes.Count - write);
        }


        public static List<XdmNode> ToNodeList(XdmValue sequence, string operation)
        {
            List<XdmNode> nodes = new List<XdmNode>(sequence.Count);
            foreach (XdmItem item in sequence)
            {
                XdmNode node = item as XdmNode;
                if (node == null)
                {
                    throw new XPathException("XPTY0004", "The operands of " + operation + " must be nodes");
                }
                nodes.Add(node);
            }
            return nodes;
        }
    }


    internal static class AxisIterator
    {
        public static bool Exists(XdmNode origin, Axis axis, NodeTest test)
        {
            switch (axis)
            {
                case Axis.Child:
                    foreach (XdmNode child in origin.ChildNodes)
                    {
                        if (test.Matches(child))
                        {
                            return true;
                        }
                    }
                    return false;
                case Axis.Attribute:
                    foreach (XdmNode attribute in origin.AttributeNodes)
                    {
                        if (test.Matches(attribute))
                        {
                            return true;
                        }
                    }
                    return false;
                case Axis.Self:
                    return test.Matches(origin);
                case Axis.Parent:
                    return origin.Parent != null && test.Matches(origin.Parent);
                default:
                    List<XdmNode> nodes = new List<XdmNode>();
                    Collect(origin, axis, test, nodes);
                    return nodes.Count > 0;
            }
        }


        /// <summary>
        /// Adds the nodes of the axis that match the test, in axis order (reverse document order for reverse axes).
        /// </summary>
        public static void Collect(XdmNode origin, Axis axis, NodeTest test, List<XdmNode> output)
        {
            switch (axis)
            {
                case Axis.Child:
                    foreach (XdmNode child in origin.ChildNodes)
                    {
                        if (test.Matches(child))
                        {
                            output.Add(child);
                        }
                    }
                    break;

                case Axis.Attribute:
                    foreach (XdmNode attribute in origin.AttributeNodes)
                    {
                        if (test.Matches(attribute))
                        {
                            output.Add(attribute);
                        }
                    }
                    break;

                case Axis.Self:
                    if (test.Matches(origin))
                    {
                        output.Add(origin);
                    }
                    break;

                case Axis.Parent:
                    if (origin.Parent != null && test.Matches(origin.Parent))
                    {
                        output.Add(origin.Parent);
                    }
                    break;

                case Axis.DescendantOrSelf:
                    if (test.Matches(origin))
                    {
                        output.Add(origin);
                    }
                    _CollectDescendants(origin, test, output);
                    break;

                case Axis.Descendant:
                    _CollectDescendants(origin, test, output);
                    break;

                case Axis.Ancestor:
                    for (XdmNode node = origin.Parent; node != null; node = node.Parent)
                    {
                        if (test.Matches(node))
                        {
                            output.Add(node);
                        }
                    }
                    break;

                case Axis.AncestorOrSelf:
                    for (XdmNode node = origin; node != null; node = node.Parent)
                    {
                        if (test.Matches(node))
                        {
                            output.Add(node);
                        }
                    }
                    break;

                case Axis.FollowingSibling:
                    if (origin.Parent != null && origin.Kind != XdmNodeKind.Attribute && origin.Kind != XdmNodeKind.Namespace)
                    {
                        XdmNode[] siblings = origin.Parent.ChildNodes;
                        for (int i = origin.Index + 1; i < siblings.Length; i++)
                        {
                            if (test.Matches(siblings[i]))
                            {
                                output.Add(siblings[i]);
                            }
                        }
                    }
                    break;

                case Axis.PrecedingSibling:
                    if (origin.Parent != null && origin.Kind != XdmNodeKind.Attribute && origin.Kind != XdmNodeKind.Namespace)
                    {
                        XdmNode[] siblings = origin.Parent.ChildNodes;
                        for (int i = origin.Index - 1; i >= 0; i--)
                        {
                            if (test.Matches(siblings[i]))
                            {
                                output.Add(siblings[i]);
                            }
                        }
                    }
                    break;

                case Axis.Following:
                    {
                        XdmNode[] all = origin.Document.AllNodes;
                        int start = (origin.Kind == XdmNodeKind.Attribute ? origin.Order : origin.EndOrder) + 1;
                        for (int i = start; i < all.Length; i++)
                        {
                            XdmNode node = all[i];
                            if (node.Kind != XdmNodeKind.Attribute && test.Matches(node))
                            {
                                output.Add(node);
                            }
                        }
                        break;
                    }

                case Axis.Preceding:
                    {
                        XdmNode[] all = origin.Document.AllNodes;
                        int start = origin.Order - 1;
                        for (int i = start; i >= 0; i--)
                        {
                            XdmNode node = all[i];
                            if (node.Kind == XdmNodeKind.Attribute || node.Kind == XdmNodeKind.Document)
                            {
                                continue;
                            }
                            if (node.EndOrder >= origin.Order)
                            {
                                // an ancestor
                                continue;
                            }
                            if (test.Matches(node))
                            {
                                output.Add(node);
                            }
                        }
                        break;
                    }

                case Axis.Namespace:
                    if (origin.Kind == XdmNodeKind.Element)
                    {
                        foreach (KeyValuePair<string, string> ns in origin.GetInScopeNamespaces())
                        {
                            XdmNode node = new XdmNode(XdmNodeKind.Namespace, origin.Document)
                            {
                                LocalName = ns.Key,
                                Value = ns.Value,
                                Parent = origin,
                                Order = origin.Order,
                                EndOrder = origin.Order
                            };
                            if (test.Matches(node))
                            {
                                output.Add(node);
                            }
                        }
                    }
                    break;
            }
        }


        private static void _CollectDescendants(XdmNode origin, NodeTest test, List<XdmNode> output)
        {
            if (origin.EndOrder <= origin.Order)
            {
                return;
            }
            string ns;
            string local;
            XdmDocument document = origin.Document;
            if (test.TryGetExactName(out ns, out local) && (test is NameTest nameTest ? nameTest.PrincipalKind == XdmNodeKind.Element : ((KindTest)test).Kind == XdmNodeKind.Element))
            {
                List<XdmNode> candidates = document.GetNodesByName(ns, local, false);
                if (candidates == null)
                {
                    return;
                }
                int index = _FirstAfter(candidates, origin.Order);
                for (int i = index; i < candidates.Count && candidates[i].Order <= origin.EndOrder; i++)
                {
                    if (test.Matches(candidates[i]))
                    {
                        output.Add(candidates[i]);
                    }
                }
                return;
            }
            XdmNode[] all = document.AllNodes;
            for (int i = origin.Order + 1; i <= origin.EndOrder; i++)
            {
                XdmNode node = all[i];
                if (node.Kind != XdmNodeKind.Attribute && test.Matches(node))
                {
                    output.Add(node);
                }
            }
        }


        /// <summary>
        /// Index of the first node with an order greater than the given one.
        /// </summary>
        private static int _FirstAfter(List<XdmNode> nodes, int order)
        {
            int low = 0;
            int high = nodes.Count;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (nodes[mid].Order <= order)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }


        /// <summary>
        /// All attributes of the descendant-or-self elements of the origin that match the test (the "//@x" step).
        /// </summary>
        public static void CollectDescendantAttributes(XdmNode origin, NodeTest test, List<XdmNode> output)
        {
            string ns;
            string local;
            if (test.TryGetExactName(out ns, out local))
            {
                List<XdmNode> candidates = origin.Document.GetNodesByName(ns, local, true);
                if (candidates == null)
                {
                    return;
                }
                int index = _FirstAfter(candidates, origin.Order);
                for (int i = index; i < candidates.Count && candidates[i].Order <= origin.EndOrder; i++)
                {
                    output.Add(candidates[i]);
                }
                return;
            }
            XdmNode[] all = origin.Document.AllNodes;
            for (int i = origin.Order + 1; i <= origin.EndOrder; i++)
            {
                XdmNode node = all[i];
                if (node.Kind == XdmNodeKind.Attribute && test.Matches(node))
                {
                    output.Add(node);
                }
            }
        }
    }


    /// <summary>
    /// The step "//@name": the attributes of the descendant-or-self nodes, evaluated with the attribute index.
    /// </summary>
    internal sealed class DescendantAttributeStepExpr : Expr
    {
        private readonly NodeTest _Test;


        public DescendantAttributeStepExpr(NodeTest test)
        {
            _Test = test;
        }


        public override ResultKind ResultKind => ResultKind.Nodes;


        public override XdmValue Evaluate(EvalContext context)
        {
            List<XdmNode> nodes = new List<XdmNode>();
            AxisIterator.CollectDescendantAttributes(context.ContextNode, _Test, nodes);
            return XdmValue.FromNodes(nodes);
        }
    }


    /// <summary>
    /// A predicate of an XSLT match pattern: a dynamic error while matching a node means the node does not match.
    /// </summary>
    internal sealed class PatternPredicateExpr : Expr
    {
        private readonly Expr _Predicate;


        public PatternPredicateExpr(Expr predicate)
        {
            _Predicate = predicate;
        }


        public override IEnumerable<Expr> Children => new[] { _Predicate };


        public override ResultKind ResultKind => _Predicate.ResultKind;


        public override XdmValue Evaluate(EvalContext context)
        {
            try
            {
                return _Predicate.Evaluate(context);
            }
            catch (XPathException)
            {
                return XdmValue.False;
            }
        }


        public override bool EffectiveBooleanValue(EvalContext context)
        {
            try
            {
                return _Predicate.EffectiveBooleanValue(context);
            }
            catch (XPathException)
            {
                return false;
            }
        }
    }
}
