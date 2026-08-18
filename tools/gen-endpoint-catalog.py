#!/usr/bin/env python3
"""
Generate api/ErpGpt.Agent/Contracts/endpoint-catalog.json from the LIVE GraphQL schema.

Run against a Development-mode instance (introspection is disabled otherwise —
HotChocolate returns HC0046), then commit the result. The validator reads the
committed file so it works in production, where introspection is off.

    ASPNETCORE_ENVIRONMENT=Development dotnet run --project api/ErpGpt.GraphQLApi
    python3 tools/gen-endpoint-catalog.py [http://localhost:5000/graphql]
"""
import json, sys, urllib.request

URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5000/graphql"
TR = "kind name ofType { kind name ofType { kind name ofType { kind name ofType { kind name } } } }"
SCALARS = {"Int","Float","String","Boolean","ID","Decimal","LocalDate","DateTime","Long","Short","Byte","UUID","Any"}

def gq(q):
    r = urllib.request.Request(URL, data=json.dumps({"query": q}).encode(),
                               headers={"Content-Type": "application/json"})
    d = json.loads(urllib.request.urlopen(r, timeout=60).read())
    if "errors" in d:
        raise SystemExit(f"introspection failed: {d['errors']}\n"
                         "Is the API running with ASPNETCORE_ENVIRONMENT=Development?")
    return d["data"]

def tname(t):
    if not t: return None
    k = t.get("kind")
    if k == "NON_NULL": return (tname(t.get("ofType")) or "") + "!"
    if k == "LIST":     return "[" + (tname(t.get("ofType")) or "") + "]"
    return t.get("name")

def base(ts): return (ts or "").replace("!","").replace("[","").replace("]","")

_out_cache, _in_cache = {}, {}

def out_fields(name):
    if name not in _out_cache:
        t = gq('{ __type(name:"%s"){ fields { name type { %s } } } }' % (name, TR))["__type"]
        _out_cache[name] = (t or {}).get("fields") or []
    return _out_cache[name]

def in_fields(name):
    if name not in _in_cache:
        t = gq('{ __type(name:"%s"){ inputFields { name type { %s } } } }' % (name, TR))["__type"]
        _in_cache[name] = (t or {}).get("inputFields") or []
    return _in_cache[name]

def operators_of(op_type):
    """Operator names a *OperationFilterInput exposes, e.g. eq/neq/gt/contains."""
    return sorted({f["name"] for f in in_fields(op_type)
                   if f["name"] not in ("and","or")})

def value_kind(op_type):
    """Coarse JSON kind a filter value must have, read from the operation input's own `eq`."""
    for f in in_fields(op_type):
        if f["name"] == "eq":
            t = base(tname(f["type"]))
            if t in ("Int","Long","Short","Byte"):        return "integer"
            if t in ("Float","Decimal"):                  return "number"
            if t == "Boolean":                            return "boolean"
            if t in ("DateTime","LocalDate"):             return "date"
            if t in ("String","UUID","ID"):               return "string"
            return t or "unknown"
    return "unknown"

# Properties EF cannot translate to SQL. HotChocolate infers them into the filter/sort inputs
# from the CLR type and knows nothing about [NotMapped], so the GraphQL request validates and
# then fails at execution. Excluding them here is the only place that knowledge can live.
NOT_MAPPED = {"displayName", "fullName", "isCurrentlySold", "isStore", "lineTotal"}

def translatable(path):
    return path.split(".")[-1] not in NOT_MAPPED

def walk_filter(type_name, prefix="", depth=0, seen=None):
    """Flatten a FilterInput into dotted paths with their legal operators.
    Depth 2 covers territory.name / person.lastName without exploding."""
    seen = seen or set()
    if depth > 1 or type_name in seen: return []
    seen = seen | {type_name}
    out = []
    for f in in_fields(type_name):
        n = f["name"]
        if n in ("and","or"): continue
        ft = base(tname(f["type"]))
        path = f"{prefix}{n}"
        if ft.endswith("OperationFilterInput"):
            if translatable(path):
                out.append({"path": path, "valueKind": value_kind(ft), "operators": operators_of(ft)})
        elif ft.startswith("ListFilterInputTypeOf"):
            continue                      # collection filters: out of scope for plan v1
        elif ft.endswith("FilterInput"):
            out.extend(walk_filter(ft, path + ".", depth + 1, seen))
    return out

def walk_sort(type_name, prefix="", depth=0, seen=None):
    seen = seen or set()
    if depth > 1 or type_name in seen: return []
    seen = seen | {type_name}
    out = []
    for f in in_fields(type_name):
        ft = base(tname(f["type"])); path = f"{prefix}{f['name']}"
        if ft.endswith("SortInput"):
            out.extend(walk_sort(ft, path + ".", depth + 1, seen))
        elif translatable(path):
            out.append(path)              # SortEnumType leaf
    return out

fields = gq("{ __schema { queryType { fields { name args { name defaultValue type { %s } } type { %s } } } } }" % (TR, TR))["__schema"]["queryType"]["fields"]

catalog = []
for f in sorted(fields, key=lambda x: x["name"]):
    if f["name"].startswith("__"): continue
    args = [{"name": a["name"], "type": tname(a["type"]),
             "required": (tname(a["type"]) or "").endswith("!") and a.get("defaultValue") is None}
            for a in f["args"]]
    ret = tname(f["type"]) or ""
    names = {a["name"] for a in args}
    family = "detail" if names == {"id"} else ("aggregation" if {"from","to"} <= names else "browse")
    shape  = "connection" if "Collection" in ret else ("list" if ret.startswith("[") else "object")

    rtype = base(ret)
    if shape == "connection":
        for ff in out_fields(rtype):
            if ff["name"] == "items":
                rtype = base(tname(ff["type"])); break

    selectable = [ff["name"] for ff in out_fields(rtype) if base(tname(ff["type"])) in SCALARS]

    filterable, sortable = [], []
    for a in f["args"]:
        at = base(tname(a["type"]))
        if a["name"] == "where" and at.endswith("FilterInput"):
            filterable = walk_filter(at)
        elif a["name"] == "order" and at.endswith("SortInput"):
            sortable = walk_sort(at)

    # enum-valued arguments (e.g. revenueByPeriod interval) with their legal values
    enums = {}
    for a in f["args"]:
        at = base(tname(a["type"]))
        if at and at not in SCALARS and not at.endswith(("FilterInput","SortInput")):
            t = gq('{ __type(name:"%s"){ kind enumValues { name } } }' % at)["__type"]
            if t and t.get("kind") == "ENUM":
                enums[a["name"]] = [v["name"] for v in t["enumValues"]]

    catalog.append({"endpoint": f["name"], "family": family, "result_shape": shape,
                    "returns": ret, "resultType": rtype, "args": args,
                    "enumArgs": enums,
                    "selectableFields": selectable,
                    "filterableFields": sorted(filterable, key=lambda x: x["path"]),
                    "sortableFields": sorted(sortable)})

open("api/ErpGpt.Agent/Contracts/endpoint-catalog.json","w").write(
    json.dumps({"generatedFrom": "GraphQL introspection", "source": URL,
                "endpoints": catalog}, indent=2) + "\n")
print(f"{len(catalog)} endpoints written")
for e in catalog:
    print(f"  {e['endpoint']:<20} {e['family']:<12} {e['result_shape']:<11} "
          f"filter={len(e['filterableFields']):<3} sort={len(e['sortableFields']):<3} enums={list(e['enumArgs'])}")
