import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect('Database/prabhupada_corpus.db')
cur = conn.cursor()

print("--- Testing Cleaner on CC, SB, and Corpus Leaks ---")

# 1. CC Chapter Ends
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE BookKey IN ('ĀDI', 'MADHYA', 'ANTYA') AND (Translation LIKE '%Thus end%' OR Purports LIKE '%Thus end%')")
cc_rows = cur.fetchall()

cc_to_clean = []
for rkey, bkey, ref, trans, purp in cc_rows:
    target_field = 'Translation' if (trans and 'Thus end' in trans) else 'Purports'
    text = trans if target_field == 'Translation' else purp
    # Match through the end of the sentence: describing ... [period]
    # Note: sometimes there is "caritāmṛ ta" or other diacritics
    m = re.search(r'(Thus end the Bhaktivedanta purports to Śrī Caitanya-carit[^\.]*Chapter[^\.]*\.)\s*(.*)$', text, re.IGNORECASE | re.DOTALL)
    if m:
        tail = m.group(2).strip()
        if tail and not tail.startswith('END OF'):
            # It's a leak!
            cleaned = text[:m.end(1)].strip()
            cc_to_clean.append((rkey, ref, target_field, text, cleaned, tail))

print(f"CC Chapter Ends to clean: {len(cc_to_clean)}")
for rkey, ref, fld, orig, cleaned, tail in cc_to_clean[:5]:
    print(f"  [{rkey}] ({ref}): tail length={len(tail)} chars: {tail[:80]}...")

# 2. SB Chapter Ends
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE BookKey = 'SB' AND (Translation LIKE '%Thus end%' OR Purports LIKE '%Thus end%')")
sb_rows = cur.fetchall()

sb_to_clean = []
for rkey, bkey, ref, trans, purp in sb_rows:
    for target_field, text in [('Translation', trans), ('Purports', purp)]:
        if not text or 'Thus end' not in text:
            continue
        # In SB: Thus end the Bhaktivedanta purports [of/to] the ... Canto, ... Chapter ... entitled "..." (or .')
        m = re.search(r'(Thus end the Bhaktivedanta purports\b.*?[\.\?]["\']?)\s*(SB\s+\d+.*)$', text, re.IGNORECASE | re.DOTALL)
        if m:
            tail = m.group(2).strip()
            cleaned = text[:m.end(1)].strip()
            sb_to_clean.append((rkey, ref, target_field, text, cleaned, tail))

print(f"SB Chapter Ends to clean: {len(sb_to_clean)}")
for rkey, ref, fld, orig, cleaned, tail in sb_to_clean[:5]:
    print(f"  [{rkey}] ({ref}): tail length={len(tail)} chars: {tail[:80]}...")

# 3. SB Canto Ends
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE BookKey = 'SB' AND (Translation LIKE '%END OF THE%' OR Purports LIKE '%END OF THE%')")
canto_rows = cur.fetchall()

canto_to_clean = []
for rkey, bkey, ref, trans, purp in canto_rows:
    for target_field, text in [('Translation', trans), ('Purports', purp)]:
        if not text or 'END OF THE' not in text:
            continue
        m = re.search(r'(END OF THE [A-Z]+ CANTO)\s*.*$', text)
        if m:
            tail = text[m.end(1):].strip()
            if tail:
                cleaned = text[:m.end(1)].strip()
                canto_to_clean.append((rkey, ref, target_field, text, cleaned, tail))

print(f"SB Canto Ends to clean: {len(canto_to_clean)}")
for rkey, ref, fld, orig, cleaned, tail in canto_to_clean:
    print(f"  [{rkey}] ({ref}): tail length={len(tail)} chars: {tail[:80]}...")

conn.close()
