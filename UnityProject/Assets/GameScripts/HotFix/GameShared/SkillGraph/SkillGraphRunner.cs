using System;
using System.Collections.Generic;
using Fantasy.Async;

namespace GameShared.SkillGraph
{
    public interface ISkillNodeHandler
    {
        FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context);
    }

    public sealed class SkillExecuteResult
    {
        public bool Completed { get; set; }

        public string NextPort { get; set; } = string.Empty;

        public static SkillExecuteResult Continue(string nextPort)
        {
            return new SkillExecuteResult
            {
                Completed = false,
                NextPort = nextPort ?? string.Empty
            };
        }

        public static SkillExecuteResult Complete()
        {
            return new SkillExecuteResult
            {
                Completed = true,
                NextPort = string.Empty
            };
        }
    }

    public sealed class SkillNodeHandlerRegistry
    {
        private readonly Dictionary<string, ISkillNodeHandler> _handlers =
            new Dictionary<string, ISkillNodeHandler>(StringComparer.OrdinalIgnoreCase);

        public void Register(string nodeType, ISkillNodeHandler handler)
        {
            if (string.IsNullOrWhiteSpace(nodeType))
                throw new ArgumentException("Node type cannot be empty.", nameof(nodeType));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _handlers[nodeType] = handler;
        }

        public bool TryGet(string nodeType, out ISkillNodeHandler? handler) =>
            _handlers.TryGetValue(nodeType ?? string.Empty, out handler);
    }

    public sealed class SkillGraphRunner
    {
        private readonly SkillNodeHandlerRegistry _handlerRegistry;

        public SkillGraphRunner(SkillNodeHandlerRegistry handlerRegistry)
        {
            _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        }

        public async FTask Run(RuntimeSkillGraph graph, SkillContext context)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Runtime == null)
                throw new InvalidOperationException("SkillContext.Runtime must be assigned before execution.");

            RuntimeSkillNode currentNode = graph.FindEntryNode();
            if (currentNode == null)
                throw new InvalidOperationException("Runtime skill graph does not contain an Entry node.");

            int executedSteps = 0;
            int maxSteps = Math.Max(16, graph.Nodes.Count * 8);
            while (currentNode != null)
            {
                if (context.IsCancelled || (context.CancellationToken != null && context.CancellationToken.IsCancel))
                    return;

                if (!_handlerRegistry.TryGet(currentNode.NodeType, out ISkillNodeHandler? handler) || handler == null)
                    throw new InvalidOperationException($"No handler registered for node type '{currentNode.NodeType}'.");

                SkillExecuteResult result = await handler.Execute(currentNode, context);
                if (result == null || result.Completed)
                    return;

                List<RuntimeConnection> nextConnections = graph.GetNextConnections(currentNode.NodeId, result.NextPort);
                if (nextConnections.Count == 0)
                    return;

                if (nextConnections.Count > 1)
                    throw new InvalidOperationException($"Node {currentNode.NodeId} port '{result.NextPort}' has multiple outgoing connections.");

                RuntimeConnection nextConnection = nextConnections[0];
                currentNode = graph.GetNode(nextConnection.ToNodeId);
                if (currentNode == null)
                    throw new InvalidOperationException($"Node {nextConnection.ToNodeId} referenced by connection was not found.");

                executedSteps++;
                if (executedSteps > maxSteps)
                    throw new InvalidOperationException($"Skill graph '{graph.SkillName}' exceeded the v0.3 execution step limit.");
            }
        }
    }
}
