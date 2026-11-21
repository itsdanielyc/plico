/* Plico redesign prototype: sample paperwork.
   Every company, person and number below is invented for the mockup.
   Pages are drawn at 600 px wide (A4 is 600 x 848). The viewer and the
   thumbnails scale the same markup, so a thumbnail is the real page. */

const A4 = { w: 600, h: 848 };

const pageFrame = (w, h, cls, inner) =>
  `<div class="pg ${cls}" style="width:${w}px;height:${h}px">${inner}</div>`;

const kw = (word) => `<span class="kw">${word}</span>`;

const rows = (items) =>
  items.map(([a, b, cls = ""]) => `<tr class="${cls}"><td>${a}</td><td class="num">${b}</td></tr>`).join("");

/* Letterhead marks are simple invented shapes, not real logos. */
const marks = {
  star: `<svg viewBox="0 0 40 40" width="34" height="34" aria-hidden="true"><path d="M20 3l4.6 11.2L36 15l-8.8 7.6L30 34l-10-6.2L10 34l2.8-11.4L4 15l11.4-.8z" fill="#1e3a5f"/></svg>`,
  bolt: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><circle cx="20" cy="20" r="19" fill="#e8a317"/><path d="M22 7l-10 15h7l-3 11 11-16h-7z" fill="#fff"/></svg>`,
  wave: `<svg viewBox="0 0 48 32" width="40" height="28" aria-hidden="true"><path d="M2 20c6-8 12-8 18 0s12 8 18 0 8-6 8-6" stroke="#0f5d63" stroke-width="5" fill="none" stroke-linecap="round"/><path d="M2 10c6-8 12-8 18 0s12 8 18 0" stroke="#3fa7a0" stroke-width="5" fill="none" stroke-linecap="round"/></svg>`,
  tooth: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><path d="M12 6c4 0 5 2 8 2s4-2 8-2c5 0 7 5 6 10-1 6-3 8-4 14-.6 4-4 5-5 0-1-4-2-7-5-7s-4 3-5 7c-1 5-4.4 4-5 0-1-6-3-8-4-14-1-5 1-10 6-10z" fill="#4c7a3d"/></svg>`,
  leaf: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><path d="M8 32C8 14 20 6 34 6c0 16-10 26-26 26z" fill="#7a5c2e"/><path d="M8 32L26 14" stroke="#fff" stroke-width="2"/></svg>`,
  school: `<svg viewBox="0 0 40 40" width="32" height="32" aria-hidden="true"><path d="M20 4l16 8v4H4v-4z" fill="#7b2d3b"/><rect x="7" y="18" width="4" height="12" fill="#7b2d3b"/><rect x="18" y="18" width="4" height="12" fill="#7b2d3b"/><rect x="29" y="18" width="4" height="12" fill="#7b2d3b"/><rect x="4" y="32" width="32" height="4" fill="#7b2d3b"/></svg>`,
  orchard: `<svg viewBox="0 0 40 40" width="28" height="28" aria-hidden="true"><circle cx="20" cy="22" r="13" fill="#3d6b35"/><rect x="19" y="4" width="2.5" height="9" fill="#5a3b1c"/></svg>`,
  bank: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><rect x="3" y="3" width="34" height="34" rx="7" fill="#24324a"/><path d="M11 28V14l9 8 9-8v14" stroke="#fff" stroke-width="3" fill="none" stroke-linejoin="round"/></svg>`,
  council: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><path d="M20 3l15 6v10c0 9-7 15-15 18C12 34 5 28 5 19V9z" fill="#3b4a7a"/><path d="M13 20l5 5 9-10" stroke="#fff" stroke-width="3" fill="none" stroke-linecap="round"/></svg>`,
  mobile: `<svg viewBox="0 0 40 40" width="30" height="30" aria-hidden="true"><circle cx="20" cy="20" r="18" fill="#1d4ed8"/><circle cx="20" cy="20" r="8" fill="none" stroke="#fff" stroke-width="4"/></svg>`,
};

const letterhead = (mark, name, lines, tone = "") => `
  <header class="lh ${tone}">
    <div class="lh-brand">${mark}<span>${name}</span></div>
    <div class="lh-addr">${lines.join("<br>")}</div>
  </header>`;

const P = {
  leaseLetter: () => pageFrame(A4.w, A4.h, "pg-serif", `
    ${letterhead(marks.star, "Northstar Properties", ["42 Quay Street", "Harbourside", "lettings@northstar.example"])}
    <div class="to">Ms D. Lindqvist<br>Flat 3, 18 Alder Row<br>Harbourside</div>
    <p class="date">14 November 2026</p>
    <h1 class="subj">Tenancy ${kw("renewal")}: Flat 3, 18 Alder Row</h1>
    <p>Dear Ms Lindqvist,</p>
    <p>Your current tenancy ends on 28 February 2027. We are pleased to offer a ${kw("renewal")} for a further twelve months on the terms below.</p>
    <table class="tbl">
      ${rows([["Current monthly rent", "£1,240.00"], ["New monthly rent from 1 March 2027", "£1,275.00"], ["Deposit held", "£1,430.00"], ["Term", "12 months"]])}
    </table>
    <p>To accept, please sign and return the enclosed ${kw("renewal")} form by <b>15 January 2027</b>. If you plan to leave, let us know by the same date so we can arrange a check-out visit.</p>
    <p>Kind regards,</p>
    <p class="sig">Maya Okafor</p>
    <p class="small">Lettings team · Ref NP-2207-18A</p>
  `),
  leaseForm: () => pageFrame(A4.w, A4.h, "pg-serif", `
    ${letterhead(marks.star, "Northstar Properties", ["Tenancy renewal form", "Ref NP-2207-18A"])}
    <h1 class="subj">${kw("Renewal")} acceptance</h1>
    <p>Property: Flat 3, 18 Alder Row, Harbourside</p>
    <p>New term: 1 March 2027 to 29 February 2028</p>
    <div class="form">
      <label><span class="box"></span> I accept the ${kw("renewal")} on the terms offered</label>
      <label><span class="box"></span> I would like to discuss the terms</label>
      <label><span class="box"></span> I will leave the property at the end of the current term</label>
    </div>
    <div class="sigline"><span>Tenant signature</span><span>Date</span></div>
    <div class="sigline"><span>Print name</span><span></span></div>
    <p class="small">Return by 15 January 2027 to lettings@northstar.example or the address above.</p>
  `),

  energyBill: (month = "October 2026", amount = "84.20", due = "5 December 2026", usage = [62, 58, 70, 81, 77, 88]) =>
    pageFrame(A4.w, A4.h, "pg-sans", `
    <div class="bill-top">${marks.bolt}<div><b>Brightwell Energy</b><span>Electricity statement</span></div><div class="acct">Account 4471 0932 18<br>Statement ${month}</div></div>
    <div class="bill-hero">
      <div><span class="lbl">Amount due</span><span class="big">£${amount}</span></div>
      <div><span class="lbl">Pay by</span><span class="mid">${due}</span></div>
      <div><span class="lbl">Payment</span><span class="mid">Direct Debit</span></div>
    </div>
    <h2 class="sec">Your usage, last six months (kWh)</h2>
    <div class="bars">${usage.map(v => `<i style="height:${v}%"></i>`).join("")}</div>
    <table class="tbl">
      ${rows([["Standing charge (31 days × 53.2p)", "£16.49"], ["Energy used: 214 kWh × 30.1p", "£64.41"], ["VAT at 5%", "£3.30"], ["Total for this period", `£${amount}`, "tot"]])}
    </table>
    <p class="small">Supply address: Flat 3, 18 Alder Row, Harbourside · Meter S14 22893</p>
  `),

  insurance: () => pageFrame(A4.w, A4.h, "pg-sans", `
    <div class="ins-top">${marks.wave}<div><b>Tidewater Mutual</b><span>Motor insurance</span></div></div>
    <h1 class="subj">Your ${kw("renewal")} schedule</h1>
    <table class="kv">
      <tr><td>Policy number</td><td>TM-88213-MOT</td></tr>
      <tr><td>Policyholder</td><td>Dana Lindqvist</td></tr>
      <tr><td>Vehicle</td><td>2019 compact hatchback · AB19 CDE</td></tr>
      <tr><td>Cover</td><td>Comprehensive</td></tr>
      <tr><td>${kw("Renewal")} date</td><td><b>1 December 2026</b></td></tr>
      <tr><td>Annual premium</td><td><b>£412.36</b></td></tr>
    </table>
    <p>Your policy will renew automatically on 1 December 2026 unless you tell us otherwise. Last year you paid £398.10.</p>
    <h2 class="sec">What's included</h2>
    <ul class="ticks"><li>Windscreen cover, £75 excess</li><li>Courtesy car while repairs are made</li><li>Personal belongings up to £300</li><li>Breakdown cover is not included</li></ul>
    <p class="small">Tidewater Mutual · 9 Pier Road, Harbourside · Call 0800 000 0000</p>
  `),

  dental: () => pageFrame(A4.w, A4.h, "pg-sans", `
    ${letterhead(marks.tooth, "Elm Street Dental", ["7 Elm Street", "Harbourside", "Invoice 26-1184"], "green")}
    <div class="to">Dana Lindqvist<br>Flat 3, 18 Alder Row</div>
    <p class="date">Invoice date 6 November 2026</p>
    <h1 class="subj">Invoice</h1>
    <table class="tbl">
      ${rows([["Examination", "£45.00"], ["Hygienist appointment (30 min)", "£62.00"], ["Total", "£107.00", "tot"], ["Paid by card, 6 Nov 2026", "−£107.00"], ["Balance", "£0.00", "tot"]])}
    </table>
    <p>Your next check-up is recommended in six months. We will send a reminder.</p>
  `),

  handNote: () => pageFrame(A4.w, A4.h, "pg-photo", `
    <div class="note">
      <p>Boiler service — Fri 13 Nov</p>
      <p>Engineer: R. Haddad</p>
      <p>Pressure ok, flue checked,</p>
      <p>new seal fitted on pump.</p>
      <p>Next service due Oct 2027</p>
      <p class="sign">Paid £85 cash</p>
    </div>
  `),

  warranty: () => pageFrame(A4.w, 400, "pg-sans pg-card", `
    <div class="war">
      <div class="war-l">${marks.leaf}<b>Halden</b><span>Appliances</span></div>
      <div class="war-r">
        <h1>Warranty registration</h1>
        <table class="kv tight">
          <tr><td>Product</td><td>Electric kettle HK-17</td></tr>
          <tr><td>Serial</td><td>HK17-2611-04418</td></tr>
          <tr><td>Purchased</td><td>2 November 2026</td></tr>
          <tr><td>Warranty</td><td>2 years, ends 2 November 2028</td></tr>
        </table>
        <p class="small">Keep this card with your proof of purchase.</p>
      </div>
    </div>
  `),

  schoolLetter: () => pageFrame(A4.w, A4.h, "pg-serif", `
    ${letterhead(marks.school, "Hillcrest Primary School", ["Hill Lane, Harbourside", "office@hillcrest.example"], "maroon")}
    <p class="date">17 November 2026</p>
    <h1 class="subj">Year 4 winter trip to the Discovery Centre</h1>
    <p>Dear parents and carers,</p>
    <p>Year 4 will visit the Discovery Centre on Thursday 10 December. The coach leaves at 9:00 and returns by 15:15. Please send a packed lunch and a waterproof coat.</p>
    <p>The trip costs £14.50, payable through the parent portal. Please return the consent slip below by <b>4 December</b>.</p>
    <p>Mrs J. Adeyemi, Year 4 lead</p>
    <div class="cut">✂ – – – – – – – – – – – – – – – – – – – – – – – – – – – – – –</div>
    <p><b>Consent slip</b> · Discovery Centre, 10 December</p>
    <div class="sigline"><span>Child's name</span><span>Class</span></div>
    <div class="sigline"><span>Parent signature</span><span>Date</span></div>
  `),

  receipt: () => pageFrame(360, 820, "pg-mono pg-receipt", `
    <div class="rc-head"><b>BLUE RIVER HARDWARE</b><span>Workshop supplies</span><span>Unit 4, Mill Yard</span></div>
    <div class="rc-rule"></div>
    <table class="rc">${rows([["Copper pipe", "24.60"], ["Brass fittings", "14.00"], ["Replacement valve", "19.90"]])}</table>
    <div class="rc-rule"></div>
    <table class="rc">${rows([["Total paid", "58.50", "tot"], ["Card ****4471", "58.50"]])}</table>
    <div class="rc-rule"></div>
    <p class="rc-big">ORDER NUMBER WRENCH470</p>
    <p>18/11/2026 10:42  Till 2</p>
    <p class="rc-thanks">Thank you for shopping with us.</p>
    <p>Keep your receipt for returns within 30 days.</p>
  `),

  payslip: (month = "November 2026", net = "2,846.12") => pageFrame(A4.w, A4.h, "pg-sans", `
    <div class="ps-top">${marks.orchard}<div><b>Orchard Studio Ltd</b><span>Payslip · ${month}</span></div><div class="acct">Employee 0047<br>D. Lindqvist</div></div>
    <div class="ps-grid">
      <table class="tbl">
        <thead><tr><td>Payments</td><td class="num">£</td></tr></thead>
        ${rows([["Basic salary", "3,650.00"], ["Overtime", "0.00"], ["Gross pay", "3,650.00", "tot"]])}
      </table>
      <table class="tbl">
        <thead><tr><td>Deductions</td><td class="num">£</td></tr></thead>
        ${rows([["Income tax", "521.40"], ["National Insurance", "209.88"], ["Pension (5%)", "72.60"], ["Total deductions", "803.88", "tot"]])}
      </table>
    </div>
    <div class="bill-hero one"><div><span class="lbl">Net pay</span><span class="big">£${net}</span></div><div><span class="lbl">Paid on</span><span class="mid">28 ${month}</span></div></div>
    <p class="small">Tax code 1257L · Period 8 · Paid to account ending 4471</p>
  `),

  bankLetter: () => pageFrame(A4.w, A4.h, "pg-sans", `
    ${letterhead(marks.bank, "Millbrook Building Society", ["PO Box 18, Millbrook", "Member services"], "navy")}
    <div class="to">Dana Lindqvist<br>Flat 3, 18 Alder Row</div>
    <p class="date">2 October 2026</p>
    <h1 class="subj">Your new debit card</h1>
    <p>Your replacement debit card is on its way and should arrive within five working days. Your PIN has not changed.</p>
    <p>Your old card ending 2290 will stop working once you use the new card for the first time.</p>
    <p class="small">Millbrook Building Society will never ask for your PIN by phone or email.</p>
  `),

  councilTax: () => pageFrame(A4.w, A4.h, "pg-sans", `
    ${letterhead(marks.council, "Westmere District Council", ["Council tax", "Account 6620 1143"], "navy")}
    <h1 class="subj">Council tax bill 2026/27</h1>
    <table class="kv"><tr><td>Property</td><td>Flat 3, 18 Alder Row</td></tr><tr><td>Band</td><td>C</td></tr><tr><td>Annual charge</td><td><b>£1,862.40</b></td></tr><tr><td>Instalments</td><td>10 monthly payments of £186.24</td></tr></table>
    <p>Payments are taken by Direct Debit on the 1st of each month from April to January.</p>
  `),

  phoneContract: () => pageFrame(A4.w, A4.h, "pg-sans", `
    <div class="ins-top">${marks.mobile}<div><b>Cobalt Mobile</b><span>Contract summary</span></div></div>
    <h1 class="subj">Your plan is due for ${kw("renewal")}</h1>
    <table class="kv"><tr><td>Number</td><td>07700 900 418</td></tr><tr><td>Plan</td><td>Unlimited calls, 30 GB data</td></tr><tr><td>Monthly price</td><td>£18.00</td></tr><tr><td>Minimum term ends</td><td><b>14 January 2027</b></td></tr></table>
    <p>You can stay on your plan month to month, or choose a ${kw("renewal")} offer from 1 December.</p>
  `),

  boilerCert: () => pageFrame(A4.w, A4.h, "pg-sans", `
    ${letterhead(marks.council, "Harbourside Heating", ["Gas safety record", "Cert 55-2026-0913"], "navy")}
    <h1 class="subj">Landlord gas safety record</h1>
    <table class="kv"><tr><td>Appliance</td><td>Combination boiler, kitchen</td></tr><tr><td>Result</td><td>Safe to use</td></tr><tr><td>Inspected</td><td>13 September 2026</td></tr><tr><td>Next inspection due</td><td>13 September 2027</td></tr></table>
  `),

  earnings: () => pageFrame(A4.w, A4.h, "pg-sans", `
    <div class="ps-top">${marks.orchard}<div><b>Orchard Studio Ltd</b><span>Annual earnings statement</span></div><div class="acct">Tax year 2025–26<br>Employee 0047</div></div>
    <div class="formgrid">
      <div><span>Total pay in this employment</span><b>£43,800.00</b></div>
      <div><span>Tax deducted</span><b>£6,256.80</b></div>
      <div><span>Employee pension contributions</span><b>£2,190.00</b></div>
      <div><span>Final tax code</span><b>1257L</b></div>
    </div>
    <p class="small">Keep this statement. You may need it to complete a tax return or claim a refund.</p>
  `),
};

/* Each document is one catalog record. `pages` holds page renderers. */
const SAMPLE_DOCUMENTS = [
  { id: "lease", title: "Tenancy renewal offer", from: "Northstar Properties", type: "Letter", date: "14 Nov 2026", added: "Today, 09:41", source: "Scanner · Office feeder", tags: [], status: "inbox", pages: [P.leaseLetter, P.leaseForm], size: "312 KB",
    path: "PDFs/Inbox/2026-11-20 Scan 0041.pdf", excerpt: `…We are pleased to offer a <mark>renewal</mark> for a further twelve months…`,
    suggest: { from: "Northstar Properties", type: "Letter", date: "14 Nov 2026", tags: ["Housing"], deadline: "Reply by 15 Jan 2027", why: "From the letterhead and 9 reviewed documents from Northstar" } },
  { id: "energy-oct", title: "Electricity statement, October", from: "Brightwell Energy", type: "Bill", date: "3 Nov 2026", added: "Today, 09:40", source: "Phone inbox", tags: [], status: "inbox", pages: [() => P.energyBill()], size: "188 KB",
    path: "PDFs/Inbox/IMG_4471.pdf", excerpt: "Amount due £84.20 · Pay by 5 December 2026",
    suggest: { from: "Brightwell Energy", type: "Bill", date: "3 Nov 2026", tags: ["Bills"], deadline: "Due 5 Dec 2026", why: "Matches your tag rule “Brightwell → Bills”" } },
  { id: "insurance", title: "Motor insurance renewal schedule", from: "Tidewater Mutual", type: "Policy", date: "2 Nov 2026", added: "Today, 09:38", source: "Added from Downloads", tags: ["Car"], status: "inbox", pages: [P.insurance], size: "96 KB",
    path: "PDFs/Inbox/renewal-schedule.pdf", excerpt: "Your <mark>renewal</mark> schedule · Renewal date 1 December 2026",
    suggest: { from: "Tidewater Mutual", type: "Policy", date: "2 Nov 2026", tags: ["Insurance"], deadline: "Renews 1 Dec 2026", why: "From the document text" } },
  { id: "note", title: "Scan 0043", from: "", type: "", date: "", added: "Today, 09:36", source: "Scanner · Flatbed", tags: [], status: "inbox", noText: true, pages: [P.handNote], size: "1.4 MB",
    path: "PDFs/Inbox/2026-11-20 Scan 0043.pdf", excerpt: "No searchable text yet" },
  { id: "dental", title: "Invoice 26-1184", from: "Elm Street Dental", type: "Invoice", date: "6 Nov 2026", added: "Yesterday", source: "Scanner · Office feeder", tags: ["Health"], status: "inbox", pages: [P.dental], size: "120 KB",
    path: "PDFs/Inbox/2026-11-19 Scan 0040.pdf", excerpt: "Examination £45.00 · Hygienist appointment",
    suggest: { from: "Elm Street Dental", type: "Invoice", date: "6 Nov 2026", tags: ["Paid"], why: "Learned from 4 reviewed invoices" } },
  { id: "kettle", title: "Kettle warranty", from: "Halden Appliances", type: "Warranty", date: "2 Nov 2026", added: "Yesterday", source: "Phone inbox", tags: ["Warranty"], status: "inbox", pages: [P.warranty], size: "74 KB",
    path: "PDFs/Inbox/IMG_4462.pdf", excerpt: "Warranty registration · Electric kettle HK-17" },
  { id: "school", title: "Year 4 winter trip", from: "Hillcrest Primary School", type: "Letter", date: "17 Nov 2026", added: "Mon 16 Nov", source: "Scanner · Office feeder", tags: [], status: "inbox", pages: [P.schoolLetter], size: "140 KB",
    path: "PDFs/Inbox/2026-11-16 Scan 0039.pdf", excerpt: "Please return the consent slip below by 4 December",
    suggest: { from: "Hillcrest Primary School", type: "Letter", date: "17 Nov 2026", tags: ["Kids"], deadline: "Reply by 4 Dec 2026", why: "From the letterhead" } },

  { id: "receipt", title: "Hardware store receipt", from: "Blue River Hardware", type: "Receipt", date: "18 Nov 2026", added: "18 Nov", source: "Scanner", tags: ["Workshop"], status: "reviewed", pages: [P.receipt], size: "64 KB", path: "PDFs/Receipts/Hardware Store Receipt.pdf", excerpt: "ORDER NUMBER WRENCH470 · Total paid 58.50" },
  { id: "payslip-nov", title: "Payslip, November", from: "Orchard Studio Ltd", type: "Payslip", date: "28 Nov 2026", added: "12 Nov", source: "Added", tags: ["Work"], status: "reviewed", pages: [() => P.payslip("November 2026")], size: "82 KB", path: "PDFs/Work/Payslip 2026-11.pdf", excerpt: "Net pay £2,846.12" },
  { id: "phone", title: "Plan renewal notice", from: "Cobalt Mobile", type: "Letter", date: "9 Nov 2026", added: "9 Nov", source: "Added", tags: ["Bills"], status: "reviewed", pages: [P.phoneContract], size: "70 KB", path: "PDFs/Bills/Cobalt plan.pdf", excerpt: "Your plan is due for <mark>renewal</mark> · Minimum term ends 14 January 2027" },
  { id: "energy-sep", title: "Electricity statement, September", from: "Brightwell Energy", type: "Bill", date: "3 Oct 2026", added: "4 Oct", source: "Phone inbox", tags: ["Bills"], status: "reviewed", pages: [() => P.energyBill("September 2026", "71.96", "5 November 2026", [70, 62, 58, 70, 81, 66])], size: "186 KB", path: "PDFs/Bills/Brightwell 2026-09.pdf", excerpt: "Amount due £71.96" },
  { id: "payslip-oct", title: "Payslip, October", from: "Orchard Studio Ltd", type: "Payslip", date: "28 Oct 2026", added: "29 Oct", source: "Added", tags: ["Work"], status: "reviewed", pages: [() => P.payslip("October 2026")], size: "82 KB", path: "PDFs/Work/Payslip 2026-10.pdf", excerpt: "Net pay £2,846.12" },
  { id: "bank", title: "New debit card", from: "Millbrook Building Society", type: "Letter", date: "2 Oct 2026", added: "3 Oct", source: "Scanner", tags: ["Bank"], status: "reviewed", pages: [P.bankLetter], size: "58 KB", path: "PDFs/Bank/New card.pdf", excerpt: "Your replacement debit card is on its way" },
  { id: "boiler", title: "Gas safety record", from: "Harbourside Heating", type: "Certificate", date: "13 Sep 2026", added: "14 Sep", source: "Scanner", tags: ["Housing"], status: "reviewed", pages: [P.boilerCert], size: "66 KB", path: "PDFs/Housing/Gas safety 2026.pdf", excerpt: "Next inspection due 13 September 2027" },
  { id: "payslip-sep", title: "Payslip, September", from: "Orchard Studio Ltd", type: "Payslip", date: "28 Sep 2026", added: "29 Sep", source: "Added", tags: ["Work"], status: "reviewed", pages: [() => P.payslip("September 2026")], size: "82 KB", path: "PDFs/Work/Payslip 2026-09.pdf", excerpt: "Net pay £2,846.12" },
  { id: "energy-aug", title: "Electricity statement, August", from: "Brightwell Energy", type: "Bill", date: "3 Sep 2026", added: "4 Sep", source: "Phone inbox", tags: ["Bills"], status: "reviewed", pages: [() => P.energyBill("August 2026", "63.10", "5 October 2026", [58, 70, 81, 66, 61, 55])], size: "184 KB", path: "PDFs/Bills/Brightwell 2026-08.pdf", excerpt: "Amount due £63.10" },
  { id: "council", title: "Council tax bill 2026/27", from: "Westmere District Council", type: "Bill", date: "12 Mar 2026", added: "14 Mar", source: "Scanner", tags: ["Housing", "Tax"], status: "reviewed", pages: [P.councilTax], size: "91 KB", path: "PDFs/Housing/Council tax 2026.pdf", excerpt: "Annual charge £1,862.40" },
  { id: "earnings", title: "Annual earnings statement 2025–26", from: "Orchard Studio Ltd", type: "Statement", date: "30 Apr 2026", added: "2 May", source: "Added", tags: ["Tax", "Work"], status: "reviewed", pages: [P.earnings], size: "77 KB", path: "PDFs/Tax/Earnings 2025-26.pdf", excerpt: "Total pay in this employment £43,800.00" },
  { id: "lease-2025", title: "Tenancy renewal offer, 2025", from: "Northstar Properties", type: "Letter", date: "12 Nov 2025", added: "Nov 2025", source: "Scanner", tags: ["Housing"], status: "reviewed", pages: [P.leaseLetter, P.leaseForm], size: "305 KB", path: "PDFs/Housing/Tenancy renewal 2025.pdf", excerpt: "…We are pleased to offer a <mark>renewal</mark> for a further twelve months…" },
];

const TAGS = [
  { name: "Bills", color: "#c2652a", count: 14 },
  { name: "Housing", color: "#3f6fb0", count: 22 },
  { name: "Tax", color: "#8a5cc2", count: 9 },
  { name: "Work", color: "#3d8a6a", count: 31 },
  { name: "Car", color: "#b8913a", count: 6 },
  { name: "Insurance", color: "#2f8f9d", count: 8 },
  { name: "Health", color: "#c24a6b", count: 7 },
  { name: "Kids", color: "#6b8f3d", count: 11 },
  { name: "Warranty", color: "#7c7c86", count: 5 },
  { name: "Workshop", color: "#9a6a3a", count: 4 },
  { name: "Bank", color: "#4a5aa8", count: 6 },
  { name: "Paid", color: "#5f7d8c", count: 18 },
];

const CORRESPONDENTS = ["Blue River Hardware", "Brightwell Energy", "Cobalt Mobile", "Elm Street Dental", "Halden Appliances", "Harbourside Heating", "Hillcrest Primary School", "Millbrook Building Society", "Northstar Properties", "Orchard Studio Ltd", "Tidewater Mutual", "Westmere District Council"];
const DOC_TYPES = ["Bill", "Certificate", "Invoice", "Letter", "Payslip", "Policy", "Receipt", "Statement", "Warranty"];
