import random
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
from editor_composition import compose
from semojum_braille import assist as a
from semojum_braille.brf import parse_brf


class CompositionTests(unittest.TestCase):
    def test_upstream_parity_and_every_source_coordinate(self):
        rng = random.Random(5917)
        for case in range(160):
            cols = rng.choice([8, 16, 32, 40])
            sources, by_id = [], {}
            for p in range(1, rng.randint(2, 5)):
                blocks = []
                for b in range(rng.randint(1, 5)):
                    ident = p * 10 + b
                    text = ''.join(rng.choice('⠁⠃⠉⠀⠀\n') for _ in range(rng.randint(1, 120)))
                    if b and rng.random() < .2:
                        text = '<!하단줄>'
                    block = dict(id=ident, order=b, braille=text,
                        breaks=list(range(2, len(text), 3)), heading_level=rng.choice([0, 1]),
                        align=rng.choice(['', 'left', 'center', 'right']))
                    blocks.append(block); by_id[ident] = text
                sources.append(dict(orig_page=p, blocks=blocks))
            job = dict(sources=sources, header=rng.choice(['', '⠋']), footer='⠚',
                opts=dict(cols=cols, rows=8, page_row_pages=rng.choice(['odd', 'every']),
                first_braille_page=rng.choice([1, 7]), cover_orig_page_count=rng.choice([0, 1]),
                pad_to_even=rng.choice([True, False]), wrap=rng.choice(['word', 'syllable'])))
            result = compose(job)
            self.assertEqual(a.build_brf_file(job).decode('ascii'), result['brf'], f'case {case}')
            self.assertEqual(parse_brf(result['brf']), result['pages'])
            for page, refs, ends in zip(result['pages'], result['maps'], result['ends']):
                for line, points, end in zip(page, refs, ends):
                    for col, point in enumerate(points):
                        if point:
                            self.assertEqual(by_id[point[0]][point[1]], line[col])
                    if end:
                        self.assertLessEqual(end[1], len(by_id[end[0]]))

    def test_crlf_offsets_and_tab_space_cells(self):
        raw = '⠁\r\n⠃\r⠉\n⠙\t⠑'
        r = compose(dict(sources=[dict(orig_page=1, blocks=[dict(id=1, braille=raw)])],
            opts=dict(rows=8, cols=16, page_row_pages='none')))
        self.assertEqual(['⠁', '⠃', '⠉', '⠙⠀⠑'], r['pages'][0][:4])
        self.assertEqual([1, 3], r['maps'][0][1][0])
        self.assertEqual([1, 8], r['maps'][0][3][1])

    def test_inline_and_repeated_breaks_keep_offsets(self):
        text = '⠁\n<!쪽바꿈>\n<!쪽바꿈>\n⠃'
        job = dict(sources=[dict(orig_page=1, blocks=[dict(id=7, braille=text)])],
            opts=dict(cols=16, rows=4, page_row_pages='none'))
        result = compose(job)
        self.assertEqual(3, len(result['pages']))
        self.assertTrue(all(not s.strip('⠀') for s in result['pages'][1]))
        self.assertEqual([7, len(text) - 1], result['maps'][2][0][0])

    def test_none_recovers_body_row_and_independent_footer(self):
        job = dict(sources=[dict(orig_page=1, blocks=[dict(id=1, braille='⠁\n' * 8)])],
            opts=dict(cols=16, rows=4, page_row_pages='none', footer_pages='none'))
        self.assertEqual(2, len(compose(job)['pages']))
        job['opts']['page_row_pages'] = 'every'
        self.assertEqual(3, len(compose(job)['pages']))
        job['opts']['page_row_pages'] = 'none'
        job['opts']['footer_pages'] = 'every'; job['footer'] = '⠃'
        r = compose(job)
        self.assertEqual(3, len(r['pages']))
        self.assertTrue(all(p[-1].strip('⠀') == '⠃' for p in r['pages']))
        self.assertTrue(all(not any(row) for page, kinds in zip(r['maps'], r['kinds']) for row, kind in zip(page, kinds) if kind == 'page'))

    def test_change_line_custom_numbers_footer_alignment(self):
        job = dict(sources=[dict(orig_page=p, blocks=[dict(id=p, braille='⠁\n' * 6)]) for p in [1, 2]],
            footer='⠃', footer_aligns={'1': 'left', '2': 'right'},
            opts=dict(cols=16, rows=4, page_row_pages='every', footer_pages='every',
                number_ranges=[[2, 2]], show_change_line=False))
        r = compose(job)
        self.assertNotIn('change', sum(r['kinds'], []))
        self.assertEqual('⠃', r['pages'][0][-1][0])
        self.assertIn('⠼', r['pages'][1][-1])
        self.assertNotIn('⠼', r['pages'][2][-1])


if __name__ == '__main__':
    unittest.main()
