using System;
using System.Collections.Generic;
using Fantasy.Async;

namespace GameShared.SkillGraph
{
    public enum SkillExecutionStatus : byte
    {
        Running = 0,
        Success = 1,
        Failure = 2,
        Cancelled = 3
    }

    public interface ISkillNodeHandler
    {
        FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context);
    }

    public sealed class SkillExecuteResult
    {
        public SkillExecutionStatus Status { get; private set; }

        public string NextPort { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        public Exception? Exception { get; private set; }

        public static SkillExecuteResult Running(string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Running,
                Message = message ?? string.Empty
            };
        }

        public static SkillExecuteResult Success(string nextPort, string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Success,
                NextPort = nextPort ?? string.Empty,
                Message = message ?? string.Empty
            };
        }

        public static SkillExecuteResult Failure(string message, Exception? exception = null)
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Failure,
                Message = message ?? string.Empty,
                Exception = exception
            };
        }

        public static SkillExecuteResult Cancelled(string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Cancelled,
                Message = message ?? string.Empty
            };
        }
    }

    public sealed class SkillGraphRunResult
    {
        public SkillExecutionStatus Status { get; private set; }

        public int LastNodeId { get; private set; }

        public int ExecutedSteps { get; private set; }

        public string Message { get; private set; } = string.Empty;

        public Exception? Exception { get; private set; }

        public bool IsSuccess => Status == SkillExecutionStatus.Success;

        public bool IsFailure => Status == SkillExecutionStatus.Failure;

        public bool IsCancelled => Status == SkillExecutionStatus.Cancelled;

        public static SkillGraphRunResult Running(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Running, lastNodeId, executedSteps, message, null);
        }

        public static SkillGraphRunResult Success(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Success, lastNodeId, executedSteps, message, null);
        }

        public static SkillGraphRunResult Failure(int lastNodeId, int executedSteps, string message, Exception? exception = null)
        {
            return Create(SkillExecutionStatus.Failure, lastNodeId, executedSteps, message, exception);
        }

        public static SkillGraphRunResult Cancelled(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Cancelled, lastNodeId, executedSteps, message, null);
        }

        private static SkillGraphRunResult Create(
            SkillExecutionStatus status,
            int lastNodeId,
            int executedSteps,
            string message,
            Exception? exception)
        {
            return new SkillGraphRunResult
            {
                Status = status,
                LastNodeId = lastNodeId,
                ExecutedSteps = executedSteps,
                Message = message ?? string.Empty,
                Exception = exception
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
        private const int MinimumExecutionStepLimit = 16;

        private readonly SkillNodeHandlerRegistry _handlerRegistry;

        public SkillGraphRunner(SkillNodeHandlerRegistry handlerRegistry)
        {
            _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        }

        public async FTask<SkillGraphRunResult> Run(RuntimeSkillGraph graph, SkillContext context)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Runtime == null)
                throw new InvalidOperationException("SkillContext.Runtime must be assigned before execution.");

            RuntimeSkillNode currentNode = graph.FindEntryNode();
            if (currentNode == null)
            {
                return SkillGraphRunResult.Failure(
                    0,
                    0,
                    $"Runtime skill graph '{graph.SkillName}' does not contain an Entry node.");
            }

            int executedSteps = 0;
            int maxSteps = ResolveMaxExecutionSteps(graph, context);
            while (currentNode != null)
            {
                if (context.IsCancellationRequested)
                {
                    context.MarkCancelled();
                    return SkillGraphRunResult.Cancelled(
                        currentNode.NodeId,
                        executedSteps,
                        $"Skill graph '{graph.SkillName}' was cancelled before node {currentNode.NodeId} executed.");
                }

                if (!_handlerRegistry.TryGet(currentNode.NodeType, out ISkillNodeHandler? handler) || handler == null)
                {
                    return SkillGraphRunResult.Failure(
                        currentNode.NodeId,
                        executedSteps,
                        $"No handler registered for node type '{currentNode.NodeType}'.");
                }

                SkillExecuteResult result;
                try
                {
                    result = await handler.Execute(currentNode, context);
                }
                catch (Exception exception)
                {
                    return SkillGraphRunResult.Failure(
                        currentNode.NodeId,
                        executedSteps,
                        $"Node {currentNode.NodeId} ('{currentNode.NodeType}') execution failed: {exception.Message}",
                        exception);
                }

                executedSteps++;
                if (executedSteps > maxSteps)
                {
                    return SkillGraphRunResult.Failure(
                        currentNode.NodeId,
                        executedSteps,
                        $"Skill graph '{graph.SkillName}' exceeded the v0.6 execution step limit ({maxSteps}).");
                }

                if (result == null)
                {
                    return SkillGraphRunResult.Failure(
                        currentNode.NodeId,
                        executedSteps,
                        $"Node {currentNode.NodeId} ('{currentNode.NodeType}') returned a null execute result.");
                }

                switch (result.Status)
                {
                    case SkillExecutionStatus.Running:
                        return SkillGraphRunResult.Running(currentNode.NodeId, executedSteps, result.Message);
                    case SkillExecutionStatus.Failure:
                        return SkillGraphRunResult.Failure(
                            currentNode.NodeId,
                            executedSteps,
                            BuildNodeResultMessage(currentNode, result, "failed"),
                            result.Exception);
                    case SkillExecutionStatus.Cancelled:
                        context.MarkCancelled();
                        return SkillGraphRunResult.Cancelled(
                            currentNode.NodeId,
                            executedSteps,
                            BuildNodeResultMessage(currentNode, result, "was cancelled"));
                    case SkillExecutionStatus.Success:
                        break;
                    default:
                        return SkillGraphRunResult.Failure(
                            currentNode.NodeId,
                            executedSteps,
                            $"Node {currentNode.NodeId} ('{currentNode.NodeType}') returned unsupported status '{result.Status}'.");
                }

                List<RuntimeConnection> nextConnections = graph.GetNextConnections(currentNode.NodeId, result.NextPort);
                if (nextConnections.Count == 0)
                {
                    return SkillGraphRunResult.Success(currentNode.NodeId, executedSteps, result.Message);
                }

                if (nextConnections.Count > 1)
                {
                    return SkillGraphRunResult.Failure(
                        currentNode.NodeId,
                        executedSteps,
                        $"Node {currentNode.NodeId} port '{result.NextPort}' has multiple outgoing connections.");
                }

                RuntimeConnection nextConnection = nextConnections[0];
                currentNode = graph.GetNode(nextConnection.ToNodeId);
                if (currentNode == null)
                {
                    return SkillGraphRunResult.Failure(
                        nextConnection.ToNodeId,
                        executedSteps,
                        $"Node {nextConnection.ToNodeId} referenced by connection was not found.");
                }
            }

            return SkillGraphRunResult.Success(0, executedSteps, $"Skill graph '{graph.SkillName}' completed.");
        }

        private static int ResolveMaxExecutionSteps(RuntimeSkillGraph graph, SkillContext context)
        {
            if (context.MaxExecutionSteps > 0)
                return context.MaxExecutionSteps;

            int nodeCount = graph.Nodes == null ? 0 : graph.Nodes.Count;
            return Math.Max(MinimumExecutionStepLimit, nodeCount * 8);
        }

        private static string BuildNodeResultMessage(RuntimeSkillNode node, SkillExecuteResult result, string fallbackVerb)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
                return result.Message;

            return $"Node {node.NodeId} ('{node.NodeType}') {fallbackVerb}.";
        }
    }
}
