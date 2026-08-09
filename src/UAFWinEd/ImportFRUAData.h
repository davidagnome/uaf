#if !defined(AFX_IMPORTFRUADATA_H__21EEA023_5118_11D4_8642_00A0CC25685D__INCLUDED_)
#define AFX_IMPORTFRUADATA_H__21EEA023_5118_11D4_8642_00A0CC25685D__INCLUDED_

#if _MSC_VER > 1000
#pragma once
#endif // _MSC_VER > 1000
// ImportFRUAData.h : header file
//

/////////////////////////////////////////////////////////////////////////////
// CImportFRUAData dialog

class CImportFRUAData : public CDialog
{
// Construction
public:
	CImportFRUAData(CWnd* pParent = NULL);   // standard constructor

  // Oracle mode (-importfrua): run OnImportfruadesign()'s work with no dialog, no folder
  // browser and no message boxes, so the .NET port in dotnet/ can be diffed against what this
  // importer actually produces. Mirrors the -dumpjson affordance; see docs/PORTING-PLAN.md.
  //
  // Deliberately a method on this class rather than a free function: ClearDesign(),
  // ImportItems() and ImportAllMonsters() all read the m_Inc*/m_Clear* members, and
  // reimplementing them outside would be a second copy of the import that could drift from the
  // one the editor really runs -- which is precisely what the diff exists to detect.
  //
  // uaPath may be empty, in which case the stock item database is not imported.
  BOOL RunHeadless(const char *dsnPath, const char *uaPath);

// Dialog Data
	//{{AFX_DATA(CImportFRUAData)
	enum { IDD = IDD_IMPORTFRUADATA };
	BOOL	m_AllInFolder;
	CString	m_UAPath;
	BOOL	m_IncItems;
	BOOL	m_IncMonsters;
	BOOL	m_ClearMonsters;
	BOOL	m_ClearItems;
	BOOL	m_IncItemsWithMonsters;
	BOOL	m_UseLoadedArt;
	//}}AFX_DATA


// Overrides
	// ClassWizard generated virtual function overrides
	//{{AFX_VIRTUAL(CImportFRUAData)
	protected:
	virtual void DoDataExchange(CDataExchange* pDX);    // DDX/DDV support
	//}}AFX_VIRTUAL

// Implementation
protected:

  BOOL ImportItems(const char *path);
  void ImportAllMonsters(const char *path);
  void ClearDesign();


	// Generated message map functions
	//{{AFX_MSG(CImportFRUAData)
	afx_msg void OnImportfruadesign();
	afx_msg void OnImportfruaitems();
	afx_msg void OnImportfruamonsters();
	afx_msg void OnImportfruaspells();
	afx_msg void OnAllinfolder();
	afx_msg void OnUaselect();
	virtual BOOL OnInitDialog();
	afx_msg void OnImportfruaart();
	//}}AFX_MSG
	DECLARE_MESSAGE_MAP()
};

//{{AFX_INSERT_LOCATION}}
// Microsoft Visual C++ will insert additional declarations immediately before the previous line.

#endif // !defined(AFX_IMPORTFRUADATA_H__21EEA023_5118_11D4_8642_00A0CC25685D__INCLUDED_)
