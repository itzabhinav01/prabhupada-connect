import sqlite3
import sys
import re
sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect('Database/prabhupada_corpus.db')
cur = conn.cursor()

# Check for patterns like "SB X.Y:" or "Antya X—" or "Madhya X—" or "Adi X—" or "BG X:" or "Chapter X:"
# appearing inside Translation or Purports
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%Thus end%' OR Purports LIKE '%Thus end%'")
rows = cur.fetchall()
print(f"Total records with 'Thus end': {len(rows)}")

leaks_found = []
for rkey, bkey, ref, trans, purp in rows:
    # check translation
    if trans and "Thus end" in trans:
        # Check if after "Thus end the Bhaktivedanta..." there is a chapter leak
        m = re.search(r'Thus end the Bhaktivedanta purports.*?\.', trans, re.IGNORECASE)
        if m:
            tail = trans[m.end():].strip()
            if tail:
                leaks_found.append((rkey, ref, 'trans', tail))
    # check purports
    if purp and "Thus end" in purp:
        m = re.search(r'Thus end the Bhaktivedanta purports.*?\.', purp, re.IGNORECASE)
        if m:
            tail = purp[m.end():].strip()
            if tail:
                # Is it just an end quote or genuine leak?
                if re.search(r'(Chapter\s+\d+|[A-Z][a-z]+\s+\d+[:—]|\bSB\s+\d+)', tail):
                    leaks_found.append((rkey, ref, 'purp', tail))

print(f"Found {len(leaks_found)} records with leaked content after 'Thus end the Bhaktivedanta purports':")
for rkey, ref, field, tail in leaks_found[:20]:
    print(f"  [{rkey}] ({ref}) in {field} ({len(tail)} chars): {tail[:80]}...")

conn.close()



