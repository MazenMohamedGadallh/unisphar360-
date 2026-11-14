import { Async, PageHeader } from '../../components/ui';
import { useApi, usePageTitle } from '../../lib/hooks';
import type { CareerRecommendation, CareerSkillGaps } from '../../types/models';

export default function CareerAssistant() {
  usePageTitle('Career Assistant');
  const recs = useApi<CareerRecommendation[]>('/api/career/recommendations');
  const gaps = useApi<CareerSkillGaps>('/api/career/skill-gaps');

  return (
    <>
      <PageHeader title="Career Assistant" subtitle="Explore career paths, skill gaps, and opportunities based on your academic profile." />

      <div className="card section-gap">
        <div className="card-header">
          <h3>Recommended Career Paths</h3>
        </div>
        <Async res={recs} loadingLabel="Loading career recommendations…" empty="No career recommendations available. Complete more courses to unlock personalized recommendations.">
          {(list) => (
            <div className="career-list">
              {list.map((r, rIdx) => (
                <div className="career-item" key={`${r.title}-${rIdx}`}>
                  <div className="career-header">
                    <h4>{r.title}</h4>
                    <span className={`match-badge ${r.matchConfidence.toLowerCase()}`}>{r.matchConfidence} Match</span>
                  </div>
                  <p className="career-desc">{r.description}</p>
                  <div className="career-details">
                    <div className="career-section">
                      <strong>Required Skills:</strong> {r.requiredSkills}
                    </div>
                    <div className="career-section">
                      <strong>Recommended Courses:</strong> {r.recommendedCourses}
                    </div>
                    <div className="career-section">
                      <strong>Internship Types:</strong> {r.internshipTypes}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </Async>
      </div>

      <div className="card">
        <div className="card-header">
          <h3>Skill Gap Analysis</h3>
        </div>
        <Async res={gaps} loadingLabel="Analyzing your skills…">
          {(g) => (
            <div className="skill-gaps-content">
              <div className="skill-section">
                <h4>Completed Courses</h4>
                {g.completedCourses.length > 0 ? (
                  <ul>
                    {g.completedCourses.map((c, cIdx) => (
                      <li key={`${c}-${cIdx}`}>{c}</li>
                    ))}
                  </ul>
                ) : (
                  <p className="empty-state">No completed courses yet.</p>
                )}
              </div>
              <div className="skill-section">
                <h4>Pending Courses</h4>
                {g.pendingCourses.length > 0 ? (
                  <ul>
                    {g.pendingCourses.map((c, cIdx) => (
                      <li key={`${c}-${cIdx}`}>{c}</li>
                    ))}
                  </ul>
                ) : (
                  <p className="empty-state">You have explored all available courses!</p>
                )}
              </div>
              <div className="skill-recommendation">
                <strong>Recommendation:</strong> {g.recommendation}
              </div>
            </div>
          )}
        </Async>
      </div>
    </>
  );
}
