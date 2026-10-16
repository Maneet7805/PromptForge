# Draws the #32 sample from WildChat-1M (ODC-BY) through the Hugging Face datasets-server API.
# Writes sample.jsonl (text stays local; only ids and labels are committed). Run: python3 sample_wildchat.py
import json, random, urllib.request, time, re
random.seed(20261016)
N=837989
API="https://datasets-server.huggingface.co/rows?dataset=allenai/WildChat-1M&config=default&split=train&offset={}&length=4"
first, later, seen = [], [], set()
import collections
prefix=collections.Counter()
def key(t): return re.sub(r'\W+',' ',t.lower())[:25]
tries=0
while (len(first)<150 or len(later)<50) and tries<400:
    tries+=1
    off=random.randrange(N-4)
    try:
        d=json.load(urllib.request.urlopen(API.format(off),timeout=30))
    except Exception as e:
        time.sleep(2); continue
    for r in d['rows']:
        row=r['row']
        if row['language']!='English' or row['toxic']: continue
        users=[(i,m) for i,m in enumerate(row['conversation']) if m['role']=='user']
        cands=[]
        if len(first)<150: cands.append(('first',users[0]))
        if len(later)<50 and len(users)>1: cands.append(('later',random.choice(users[1:])))
        for kind,(i,m) in cands:
            t=m['content'].strip()
            p=' '.join(re.sub(r'[^\w ]+',' ',t.lower()).split()[:4])
            if not (5<=len(t)<=2000) or key(t) in seen or prefix[p]>=2: continue
            seen.add(key(t)); prefix[p]+=1
            rec={'id':f"{row['conversation_hash'][:10]}-{i}",'kind':kind,'text':t}
            (first if kind=='first' else later).append(rec)
            break
print(len(first),len(later),tries)
with open('sample.jsonl','w') as f:
    for r in first[:150]+later[:50]: f.write(json.dumps(r)+'\n')
