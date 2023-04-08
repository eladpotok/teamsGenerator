import './MyCard.css'

function MyCard(props) {

    const classes = 'card ' + props.className;

    return (  <div className={classes}> {props.children} </div> );

}


export default MyCard;