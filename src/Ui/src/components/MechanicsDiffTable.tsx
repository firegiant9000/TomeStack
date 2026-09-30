import type { UpdateReview } from '../api/types';

/** The rule changes between two revisions: properties and effects by id (SPEC I-06; the update review and M5 slice 4's comparison). */
export function MechanicsDiffTable({ mechanics }: { mechanics: UpdateReview['mechanics'] }) {
  if (mechanics.properties.length === 0 && mechanics.effects.length === 0) return <p>The rules are the same.</p>;
  return (
    <table>
      <caption>Rule changes</caption>
      <thead>
        <tr>
          <th scope="col">Effect or property</th>
          <th scope="col">Change</th>
          <th scope="col">Before</th>
          <th scope="col">After</th>
        </tr>
      </thead>
      <tbody>
        {mechanics.properties.map((p) => (
          <tr key={`p-${p.property}`}>
            <td>{p.property}</td>
            <td>changed</td>
            <td>{p.before ?? ''}</td>
            <td>{p.after ?? ''}</td>
          </tr>
        ))}
        {mechanics.effects.map((e) => (
          <tr key={`e-${e.effectId}`}>
            <td>{e.effectId}</td>
            <td>{e.change}</td>
            <td>
              <code>{e.before ?? ''}</code>
            </td>
            <td>
              <code>{e.after ?? ''}</code>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
