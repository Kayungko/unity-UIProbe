using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UIProbe.Core.Contract;
using UIProbe.Infrastructure.UnityAdapters;

namespace UIProbe.Core.Services
{
    /// <summary>
    /// 工具体系中枢:所有工具集中注册,MCP 与 UI 必须经它发现与调用,不得绕过。
    /// Adapter 经构造注入并在反射构造 [UIProbeTool] 工具时转交工具 ctor;
    /// ToolContext 是冻结契约(纯 cross-cutting,无 Adapter 槽位),故不经 ToolContext 传 Adapter。
    /// 本骨架打通只读调用路径,真实业务 Service 留 M2。
    /// </summary>
    public sealed class ToolRegistry
    {
        private readonly Dictionary<string, ToolRegistration> _byId =
            new Dictionary<string, ToolRegistration>(StringComparer.Ordinal);

        private readonly IAssetGateway _assetGateway;
        private readonly IFileSystem _fileSystem;
        private readonly IEditorPrefs _editorPrefs;

        public ToolRegistry(IAssetGateway assetGateway, IFileSystem fileSystem, IEditorPrefs editorPrefs)
        {
            _assetGateway = assetGateway;
            _fileSystem = fileSystem;
            _editorPrefs = editorPrefs;
        }

        /// <summary>登记一个已构造好的工具。同 Descriptor.Id 重复注册抛异常。</summary>
        public void Register(IUIProbeTool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }
            ToolDescriptor descriptor = tool.Descriptor;
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id))
            {
                throw new ArgumentException("工具缺少 Descriptor.Id,无法注册");
            }
            if (_byId.ContainsKey(descriptor.Id))
            {
                throw new InvalidOperationException("工具 Id 重复注册: " + descriptor.Id);
            }
            _byId[descriptor.Id] = new ToolRegistration { Descriptor = descriptor, Tool = tool };
        }

        /// <summary>列出工具描述符。传入 profile 时过滤掉 MinProfile 高于该档位的工具。</summary>
        public List<ToolDescriptor> ListTools(CapabilityProfile? profile = null)
        {
            IEnumerable<ToolRegistration> regs = _byId.Values;
            if (profile.HasValue)
            {
                int granted = (int)profile.Value;
                regs = regs.Where(r => (int)r.Descriptor.MinProfile <= granted);
            }
            return regs.Select(r => r.Descriptor).ToList();
        }

        /// <summary>
        /// 返回单个工具描述。命中:Success + Data 填 descriptor JSON;缺失:Failed + TOOL_NOT_FOUND。
        /// 返回 ToolResult(而非裸 ToolDescriptor)以便承载缺失错误码,与 Invoke 表达一致。
        /// </summary>
        public ToolResult DescribeTool(string id)
        {
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out ToolRegistration reg))
            {
                return NotFound(id);
            }
            return new ToolResult
            {
                Status = ToolStatus.Success,
                Data = JsonUtility.ToJson(reg.Descriptor)
            };
        }

        /// <summary>
        /// 按 ToolId 查找并调用。缺失回 TOOL_NOT_FOUND;命中则构造 ToolContext 并委派 tool.Run。
        /// Validate / Phase 路由发生在工具的 Run(ToolRunnerBase),Registry 不重复做。
        /// </summary>
        public ToolResult Invoke(ToolRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ToolId))
            {
                return NotFound(request != null ? request.ToolId : null);
            }
            if (!_byId.TryGetValue(request.ToolId, out ToolRegistration reg))
            {
                return NotFound(request.ToolId);
            }
            var ctx = new ToolContext { CorrelationId = request.CorrelationId };
            return reg.Tool.Run(request, ctx);
        }

        /// <summary>
        /// Attribute 驱动发现:扫描程序集中标注 [UIProbeTool] 的类型,按 ctor 签名选择性注入
        /// Adapter 后构造并注册。便于项目方扩展自有工具。
        /// </summary>
        public void RegisterFromAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }
            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.GetCustomAttribute<UIProbeToolAttribute>() == null)
                {
                    continue;
                }
                if (!typeof(IUIProbeTool).IsAssignableFrom(type))
                {
                    throw new InvalidOperationException(
                        "[UIProbeTool] 标注的类型未实现 IUIProbeTool: " + type.FullName);
                }
                var tool = (IUIProbeTool)Construct(type);
                Register(tool);
            }
        }

        // 选取参数全部可由已知 Adapter 满足的 ctor(取可满足参数最多者),按类型匹配注入。
        private object Construct(Type type)
        {
            ConstructorInfo best = null;
            object[] bestArgs = null;
            foreach (ConstructorInfo ctor in type.GetConstructors())
            {
                ParameterInfo[] ps = ctor.GetParameters();
                var args = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (!TryResolve(ps[i].ParameterType, out args[i]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok && (best == null || ps.Length > best.GetParameters().Length))
                {
                    best = ctor;
                    bestArgs = args;
                }
            }
            if (best == null)
            {
                throw new InvalidOperationException(
                    "无法构造工具 " + type.FullName + ":没有参数全部可由 Adapter 满足的构造函数");
            }
            return best.Invoke(bestArgs);
        }

        private bool TryResolve(Type parameterType, out object value)
        {
            if (parameterType == typeof(IAssetGateway)) { value = _assetGateway; return true; }
            if (parameterType == typeof(IFileSystem)) { value = _fileSystem; return true; }
            if (parameterType == typeof(IEditorPrefs)) { value = _editorPrefs; return true; }
            value = null;
            return false;
        }

        private static ToolResult NotFound(string id)
        {
            string msg = "未找到工具: " + (id ?? "<null>");
            return new ToolResult
            {
                Status = ToolStatus.Failed,
                Message = msg,
                Error = new ToolError
                {
                    Code = ToolErrorCodes.ToolNotFound,
                    Message = msg,
                    Retriable = false
                }
            };
        }
    }
}
